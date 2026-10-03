import AppKit

// One Onkey on screen: his window, where he's walking, and his eyes, blinks and voice.
// The drawing, settings and menu are shared through the app.
final class Pet {
    unowned let app: OnkeyApp
    let window: NSWindow
    let view: PetView
    var area: NSRect
    var px: Double, py: Double
    private var targetX = 0.0, targetY = 0.0
    private var restUntil = 0.0, nextSound = 0.0
    private(set) var dragging = false
    private var shown: CGImage?
    private var shownBounce: CGFloat = 0
    // His arms, and whether they need drawing again whatever the rig says (e.g. after a resize).
    private let rig = ArmRig()
    private var armsStale = true
    private var shownOrigin: NSPoint?
    // What the eyes and mouth last showed, so unchanged ones aren't redrawn every tick.
    private var shownPupils: [CGPoint]?
    private var lidsShown = true, mouthShown = true
    private var shownSquash: CGFloat = 0
    private var gaze: [CGPoint] = [.zero, .zero]   // Pupil offsets in sprite pixels, y-down.
    private var blinkStart = -1.0, nextBlink = ProcessInfo.processInfo.systemUptime + Double.random(in: 2...8)
    // Each Onkey has his own copy of the sound so several can "oooo" at once.
    private let sound: NSSound?
    private var soundStart = -Double.infinity
    private var mouthOpen = 0.0
    var onMoved: (() -> Void)?
    var backingObserver: NSObjectProtocol?

    private var size: CGFloat { app.size }
    private var windowSize: NSSize { app.windowSize }

    init(app: OnkeyApp, origin: NSPoint, area: NSRect) {
        self.app = app
        self.area = area
        px = origin.x; py = origin.y
        sound = app.baseSound?.copy() as? NSSound
        window = NSWindow(contentRect: NSRect(origin: origin, size: app.windowSize),
                          styleMask: .borderless, backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.isReleasedWhenClosed = false
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        view = PetView(frame: NSRect(origin: .zero, size: app.windowSize))
        view.onDrag = { [weak self] origin in
            guard let self else { return }
            self.dragging = true
            self.px = origin.x; self.py = origin.y
            self.window.setFrameOrigin(origin)
            self.shownOrigin = origin
        }
        view.onDragEnd = { [weak self] in
            guard let self else { return }
            self.dragging = false
            if let screen = self.window.screen { self.area = screen.visibleFrame }
            self.restUntil = self.app.clock + 3
            self.pickTarget()
            self.onMoved?()
        }
        view.onClick = { [weak self] in self?.playSound(force: true) }
        window.contentView = view
        applyAppearance()
        clampToArea()
        pickTarget()
        nextSound = app.clock + soundDelay()
        present(app.idle)
        window.orderFrontRegardless()
    }

    func close() {
        sound?.stop()
        window.orderOut(nil)
        window.close()
    }

    func applyAppearance() {
        let d = app.defaults
        window.alphaValue = CGFloat(d.double(forKey: Key.opacity))
        window.level = d.string(forKey: Key.layer) == "desktop"
            ? NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1)
            : .floating
        window.ignoresMouseEvents = !d.bool(forKey: Key.draggable)
        // Without .fullScreenAuxiliary he stays out of full-screen apps' spaces.
        window.collectionBehavior = d.bool(forKey: Key.overFullScreen)
            ? [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
            : [.canJoinAllSpaces, .stationary, .ignoresCycle]
    }

    // After a size change: keep him centred where he was, at the new size.
    func resize(from oldSize: NSSize) {
        let center = NSPoint(x: px + Double(oldSize.width) / 2, y: py + Double(oldSize.height) / 2)
        window.setContentSize(windowSize)
        view.frame = NSRect(origin: .zero, size: windowSize)
        shown = nil
        forgetShown()
        px = center.x - windowSize.width / 2
        py = center.y - windowSize.height / 2
        clampToArea()
        pickTarget()
        present(app.idle)
    }

    func framesChanged() { shown = nil; forgetShown(); present(app.idle) }

    // MARK: Arms

    // Where the canvas's top-left is in "world" sprite pixels (y-down), so carrying him
    // swings his arms.
    private var worldOrigin: CGPoint {
        let k = Double(size) * OnkeyRenderer.spriteScale
        return CGPoint(x: px / k, y: -(py + Double(windowSize.height)) / k)
    }

    func updateArms(dt: Double) {
        let prefs = app.prefs
        let changed = rig.update(dt: dt, origin: worldOrigin, carried: dragging, floppy: prefs.floppyArms,
                                 sketchy: prefs.sketchy)
        guard changed || armsStale else { return }
        armsStale = false
        shownBounce = CGFloat(rig.bounce)
        view.setBodyLift(shownBounce * size)
        let k = size * CGFloat(OnkeyRenderer.spriteScale)
        view.showArms(rig.shapes(sketchy: prefs.sketchy), poses: rig.poses, hands: app.renderer.hands,
                      map: OnkeyRenderer.spriteToView(size: size), pointsPerPixel: k, contentsScale: 1 / k)
        // The eyes, lids and mouth bob with his head.
        shownPupils = nil; lidsShown = true; mouthShown = true
    }

    // Squashes him for the beat (0 normal, 0.15 is 15% shorter).
    func bop(_ squash: CGFloat) {
        if abs(squash - shownSquash) < 0.0005 { return }
        view.setSquash(squash)
        shownSquash = squash
    }

    // Makes the next tick redraw everything, e.g. after a resize or a drag.
    private func forgetShown() { shownOrigin = nil; shownPupils = nil; lidsShown = true; mouthShown = true }

    // MARK: Movement

    // Allowed window origins, letting the transparent canvas margin hang off-screen
    // so Onkey's hands can touch the very edge.
    private var limits: (minX: Double, maxX: Double, minY: Double, maxY: Double) {
        let s = size, h = OnkeyRenderer.canvas.height, b = app.petBounds
        let minX = area.minX - b.minX * s
        let maxX = max(minX, area.maxX - b.maxX * s)
        let minY = area.minY - (h - b.maxY) * s
        let maxY = max(minY, area.maxY - (h - b.minY) * s)
        return (minX, maxX, minY, maxY)
    }

    func clampToArea() {
        let l = limits
        px = min(l.maxX, max(l.minX, px))
        py = min(l.maxY, max(l.minY, py))
    }

    func pickTarget() {
        let l = limits
        let zone = app.prefs.zone
        if zone == "stay" { targetX = px; targetY = py; return }
        var x = Double.random(in: l.minX...l.maxX), y = Double.random(in: l.minY...l.maxY)
        let chase = app.prefs.chase
        let mouse = NSEvent.mouseLocation
        if chase > 0 && Int.random(in: 0..<chase) == 0 && area.contains(mouse) {
            x = min(l.maxX, max(l.minX, mouse.x - windowSize.width / 2))
            y = min(l.maxY, max(l.minY, mouse.y - windowSize.height / 2))
        }
        switch zone {
        case "bottom": y = l.minY
        case "top": y = l.maxY
        case "left": x = l.minX
        case "right": x = l.maxX
        default: break
        }
        targetX = x; targetY = y
    }

    func restFor(_ seconds: Double) { restUntil = app.clock + seconds }
    func rescheduleSound() { nextSound = app.clock + soundDelay() }

    func walk(dt: Double) {
        let clock = app.clock
        if clock >= nextSound { playSound(force: false); nextSound = clock + soundDelay() }
        if clock < restUntil || app.prefs.zone == "stay" { present(app.idle); return }
        let dx = targetX - px, dy = targetY - py
        let distance = (dx * dx + dy * dy).squareRoot()
        if distance < 3 {
            restUntil = clock + 2 + Double.random(in: 0...4)
            pickTarget()
            present(app.idle)
            return
        }
        let speed = app.prefs.speed * Double(size)
        let step = min(distance, speed * dt)
        px += step * dx / distance
        py += step * dy / distance
        // His hands step as far as he goes, so he never looks like he's skating.
        let k = Double(size) * OnkeyRenderer.spriteScale
        rig.walked(dx: step * dx / distance / k, distance: step / k)
        present(app.idle)
    }

    func present(_ image: CGImage) {
        if shown !== image {
            view.show(image, scale: app.pixelScale / size)
            // The bottom of his hands, which stays put when he bops.
            view.setBase((OnkeyRenderer.canvas.height - app.idleBottom) * size)
            shown = image
            armsStale = true
        }
        let origin = NSPoint(x: px.rounded(), y: py.rounded())
        if origin != shownOrigin { window.setFrameOrigin(origin); shownOrigin = origin }
    }

    // MARK: Eyes, eyelids and mouth

    // Eye centre (plus a pupil offset) in view points, y-up, for the frame on screen.
    private func eyePoint(_ eye: CGPoint, offset: CGPoint) -> CGPoint {
        let c = OnkeyRenderer.canvasPoint(CGPoint(x: eye.x + offset.x, y: eye.y + offset.y), bounce: shownBounce)
        return CGPoint(x: c.x * size, y: (OnkeyRenderer.canvas.height - c.y) * size)
    }

    // Each pupil eases toward the cursor, so he goes a bit cross-eyed when it's close.
    func updateFace(dt: Double) {
        let pointsPerPixel = (OnkeyRenderer.canvasPoint(CGPoint(x: 1, y: 0), bounce: 0).x
            - OnkeyRenderer.canvasPoint(.zero, bounce: 0).x) * size
        updateLids(pointsPerPixel: pointsPerPixel)
        updateMouth(dt: dt)
        guard app.watching else {
            if shownPupils != [] { view.showPupils([], at: nil, pointsPerPixel: 0, contentsScale: 1); shownPupils = [] }
            return
        }
        let mouse = NSEvent.mouseLocation
        let origin = window.frame.origin
        let ease = CGFloat(1 - exp(-dt * 14))
        var centers: [CGPoint] = []
        for (i, eye) in app.renderer.pupils.map(\.center).enumerated() {
            let here = eyePoint(eye, offset: .zero)
            let dx = mouse.x - (origin.x + here.x), dy = mouse.y - (origin.y + here.y)
            let distance = (dx * dx + dy * dy).squareRoot()
            // Full travel once the cursor is a few eye-widths away; centred when it's on the eye.
            let reach = OnkeyRenderer.pupilTravel * min(1, distance / (25 * size))
            let target = distance < 0.5 ? CGPoint.zero
                : CGPoint(x: dx / distance * reach, y: -dy / distance * reach)
            gaze[i].x += (target.x - gaze[i].x) * ease
            gaze[i].y += (target.y - gaze[i].y) * ease
            centers.append(eyePoint(eye, offset: gaze[i]))
        }
        // Resting Onkeys with a still cursor have nothing new to show.
        if let last = shownPupils, last.count == centers.count,
           zip(last, centers).allSatisfy({ abs($0.x - $1.x) < 0.01 && abs($0.y - $1.y) < 0.01 }) { return }
        shownPupils = centers
        view.showPupils(app.renderer.pupils, at: centers, pointsPerPixel: pointsPerPixel,
                        contentsScale: 1 / pointsPerPixel)
    }

    // Blinks every 6-14 seconds: the eye on the left of the screen first, then the right
    // one following close behind, closing while the first is still shut. Each blink is a
    // quick close, a beat shut, and open (0.37 s in all).
    private static let blinkOrder: [Double] = [0, 0.14]   // Delay per eye: [left, right] on screen.

    private func updateLids(pointsPerPixel k: CGFloat) {
        let now = ProcessInfo.processInfo.systemUptime
        if app.prefs.blink && now >= nextBlink {
            blinkStart = now
            nextBlink = now + 6 + Double.random(in: 0...8)
        }
        // Between blinks (nearly all the time) the lids stay hidden and need no work.
        if now - blinkStart > 0.5 && !lidsShown { return }
        var closure: [CGFloat] = []
        var frames: [CGRect] = []
        for (i, interior) in app.renderer.eyeInteriors.enumerated() {
            let t = now - blinkStart - Self.blinkOrder[i]
            let c: Double
            switch t {
            case ..<0: c = 0
            case ..<0.11: c = t / 0.11
            case ..<0.21: c = 1
            case ..<0.37: c = 1 - (t - 0.21) / 0.16
            default: c = 0
            }
            closure.append(CGFloat(c))
            let r = interior.rect
            let topLeft = OnkeyRenderer.canvasPoint(CGPoint(x: r.minX, y: r.minY), bounce: shownBounce)
            frames.append(CGRect(x: topLeft.x * size, y: (OnkeyRenderer.canvas.height - topLeft.y) * size - r.height * k,
                                 width: r.width * k, height: r.height * k))
        }
        view.showLids(app.renderer.eyeInteriors, frames: frames, closure: closure,
                      color: app.renderer.lidColor, pointsPerPixel: k)
        lidsShown = closure.contains { $0 > 0.01 }
    }

    // Opens his mouth "just a tad", following how loud the clip is at this moment.
    private func updateMouth(dt: Double) {
        let envelope = app.soundEnvelope
        let t = ProcessInfo.processInfo.systemUptime - soundStart
        // t is infinite until the first sound plays; only index the clip while it's playing.
        let playing = t >= 0 && t < Double(envelope.count) / 60
        let target = playing ? min(1, envelope[Int(t * 60)] * 1.25) : 0
        mouthOpen += (target - mouthOpen) * (1 - exp(-dt * 25))
        if mouthOpen <= 0.02 && !mouthShown { return }
        mouthShown = mouthOpen > 0.02
        let s0 = OnkeyRenderer.canvasPoint(CGPoint(x: 1, y: 0), bounce: 0).x - OnkeyRenderer.canvasPoint(.zero, bounce: 0).x
        let origin = OnkeyRenderer.canvasPoint(.zero, bounce: shownBounce)
        // Sprite pixels (y-down) to view points (y-up), following his head's bounce.
        let map = CGAffineTransform(a: s0 * size, b: 0, c: 0, d: -s0 * size,
                                    tx: origin.x * size, ty: (OnkeyRenderer.canvas.height - origin.y) * size)
        view.showMouth(open: CGFloat(mouthOpen), spriteToView: map)
    }

    // MARK: Sound

    private func soundDelay() -> Double {
        let gap = app.prefs.soundGap
        return gap + Double.random(in: 0...gap)
    }

    func playSound(force: Bool) {
        guard let sound, force || app.prefs.soundOn else { return }
        sound.volume = Float(app.prefs.volume)
        sound.stop()
        sound.play()
        soundStart = ProcessInfo.processInfo.systemUptime
    }

    func stopSound() {
        sound?.stop()
        soundStart = -.infinity
    }
}
