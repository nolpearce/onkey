// Onkey desktop pet for macOS. Mirrors the Windows version in Onkey.cs:
// a transparent window that wanders the screen, rests between trips, and plays
// its sound now and then. Everything is adjustable from the menu bar icon.
import AppKit
import AVFoundation
import ServiceManagement

// MARK: - Rendering

// Port of OnkeyRenderer from Onkey.cs. Arm masks separate the arms from the ears
// and head in Onkey.png so each arm can swing about its shoulder. Rendering from
// the full-size sprite keeps Onkey sharp at every size and on Retina screens.
struct RenderedFrame {
    let image: CGImage
    let opaqueBounds: CGRect   // In canvas points, y-down.
}

// A shape lifted from the sprite as black ink with its original soft edge:
// the drawn pupils, and the inside of each eye outline (used to clip eyelids).
struct Cutout {
    let image: CGImage
    let center: CGPoint   // Ink-weighted centre, in sprite pixels (y-down).
    let rect: CGRect      // Bounds of the image, in sprite pixels (y-down).

    init?(ink: [(x: Int, y: Int, alpha: UInt8)]) {
        let minX = ink.map(\.x).min()!, maxX = ink.map(\.x).max()!
        let minY = ink.map(\.y).min()!, maxY = ink.map(\.y).max()!
        let w = maxX - minX + 1, h = maxY - minY + 1
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let data = ctx.data?.assumingMemoryBound(to: UInt8.self) else { return nil }
        var sumX = 0.0, sumY = 0.0, weight = 0.0
        for p in ink {
            // Bitmap memory is top row first, matching the sprite's y-down rows.
            data[(p.y - minY) * ctx.bytesPerRow + (p.x - minX) * 4 + 3] = p.alpha
            sumX += Double(p.x) * Double(p.alpha); sumY += Double(p.y) * Double(p.alpha)
            weight += Double(p.alpha)
        }
        guard let image = ctx.makeImage(), weight > 0 else { return nil }
        self.image = image
        center = CGPoint(x: sumX / weight, y: sumY / weight)
        rect = CGRect(x: minX, y: minY, width: w, height: h)
    }
}

final class OnkeyRenderer {
    static let canvas = CGSize(width: 180, height: 132)
    static let frameCount = 40
    private static let source = CGSize(width: 1774, height: 887)
    private static let petWidth: CGFloat = 140
    // The moving pupils are the original drawn pupils shrunk slightly to leave
    // room to look around. Travel is in sprite pixels.
    static let pupilScale: CGFloat = 0.86
    static let pupilTravel: CGFloat = 13
    private let head, leftArm, rightArm: CGImage
    // The head with its drawn pupils painted white, and the pupils cut out of it.
    private let blankEyedHead: CGImage
    let pupils: [Cutout]
    // The area inside each eye's outline, and the skin colour just above the eyes.
    let eyeInteriors: [Cutout]
    let lidColor: CGColor

    init?(spriteURL: URL) {
        guard let src = CGImageSourceCreateWithURL(spriteURL as CFURL, nil),
              let sprite = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
        let left = CGMutablePath()
        left.addLines(between: [CGPoint(x: 0, y: 600), CGPoint(x: 330, y: 600),
                                CGPoint(x: 410, y: 722), CGPoint(x: 565, y: 722),
                                CGPoint(x: 620, y: 752), CGPoint(x: 643, y: 778),
                                CGPoint(x: 643, y: 887), CGPoint(x: 0, y: 887)])
        left.closeSubpath()
        let right = CGMutablePath()
        right.addLines(between: [CGPoint(x: 1137, y: 762), CGPoint(x: 1200, y: 748),
                                 CGPoint(x: 1340, y: 730), CGPoint(x: 1440, y: 620),
                                 CGPoint(x: 1774, y: 620), CGPoint(x: 1774, y: 887),
                                 CGPoint(x: 1137, y: 887)])
        right.closeSubpath()
        let everything = CGMutablePath()
        everything.addRect(CGRect(origin: .zero, size: Self.source))
        everything.addPath(left)
        everything.addPath(right)
        guard let l = Self.cut(sprite, left, evenOdd: false),
              let r = Self.cut(sprite, right, evenOdd: false),
              let h = Self.cut(sprite, everything, evenOdd: true) else { return nil }
        leftArm = l; rightArm = r; head = h
        guard let eyes = Self.erasePupils(h) else { return nil }
        blankEyedHead = eyes.head; pupils = eyes.pupils
        eyeInteriors = eyes.interiors; lidColor = eyes.lidColor
    }

    // A bitmap context whose coordinates run y-down, matching the GDI+ maths.
    private static func flippedContext(width: Int, height: Int) -> CGContext? {
        guard let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                  bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.translateBy(x: 0, y: CGFloat(height))
        ctx.scaleBy(x: 1, y: -1)
        ctx.interpolationQuality = .high
        return ctx
    }

    private static func drawUpright(_ ctx: CGContext, _ image: CGImage, in rect: CGRect) {
        ctx.saveGState()
        ctx.translateBy(x: rect.minX, y: rect.maxY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.draw(image, in: CGRect(origin: .zero, size: rect.size))
        ctx.restoreGState()
    }

    private static func cut(_ sprite: CGImage, _ path: CGPath, evenOdd: Bool) -> CGImage? {
        guard let ctx = flippedContext(width: Int(source.width), height: Int(source.height)) else { return nil }
        ctx.addPath(path)
        ctx.clip(using: evenOdd ? .evenOdd : .winding)
        drawUpright(ctx, sprite, in: CGRect(origin: .zero, size: source))
        return ctx.makeImage()
    }

    // Flood-fills each pupil (and its grey anti-aliased rim) with white, stopping at
    // the white ring inside the eye outline, and keeps what was removed as black ink
    // so the moving pupils keep their hand-drawn edges. Seeds are inside each pupil.
    private static func erasePupils(_ head: CGImage)
        -> (head: CGImage, pupils: [Cutout], interiors: [Cutout], lidColor: CGColor)? {
        let w = Int(source.width), h = Int(source.height)
        guard let ctx = flippedContext(width: w, height: h),
              let data = ctx.data?.assumingMemoryBound(to: UInt8.self) else { return nil }
        drawUpright(ctx, head, in: CGRect(origin: .zero, size: source))
        let stride = ctx.bytesPerRow
        var pupils: [Cutout] = []
        for seed in [(753, 525), (1056, 512)] {
            var visited = Set<Int>(), stack = [seed]
            var ink: [(x: Int, y: Int, alpha: UInt8)] = []
            while let (x, y) = stack.popLast() {
                let dx = x - seed.0, dy = y - seed.1
                guard x >= 0, y >= 0, x < w, y < h, dx * dx + dy * dy < 75 * 75,
                      visited.insert(y * w + x).inserted else { continue }
                let p = data + y * stride + x * 4
                let a = Int(p[3]), m = Int(max(p[0], p[1], p[2]))
                guard a > 100, m < 200 else { continue }
                // How dark the pixel is becomes how opaque the ink is.
                let lightness = min(255, m * 255 / a)
                ink.append((x, y, UInt8((255 - lightness) * a / 255)))
                p[0] = p[3]; p[1] = p[3]; p[2] = p[3]   // Premultiplied white.
                stack.append(contentsOf: [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)])
            }
            guard ink.count > 1000, let art = Cutout(ink: ink) else { return nil }
            pupils.append(art)
        }
        // With the pupils gone, each eye's inside is one light patch bounded by the dark
        // outline (and by the muzzle's outline where the muzzle overlaps the eye).
        var interiors: [Cutout] = []
        for seed in [(753, 525), (1056, 512)] {
            var visited = Set<Int>(), stack = [seed]
            var ink: [(x: Int, y: Int, alpha: UInt8)] = []
            while let (x, y) = stack.popLast() {
                let dx = x - seed.0, dy = y - seed.1
                guard x >= 0, y >= 0, x < w, y < h, dx * dx + dy * dy < 85 * 85,
                      visited.insert(y * w + x).inserted else { continue }
                let p = data + y * stride + x * 4
                let a = Int(p[3])
                guard a > 100 else { continue }
                let lightness = Int(max(p[0], p[1], p[2])) * 255 / a
                let greyness = Int(max(p[0], p[1], p[2])) - Int(min(p[0], p[1], p[2]))
                guard lightness >= 60, greyness * 255 / a < 60 else { continue }
                // Fully cover everything but the darkest edge pixels, so no pale ring shows.
                ink.append((x, y, UInt8(min(255, (lightness - 60) * 255 / 50))))
                stack.append(contentsOf: [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)])
            }
            guard ink.count > 1000, let interior = Cutout(ink: ink) else { return nil }
            interiors.append(interior)
        }
        // Average skin colour in a patch of forehead above the left eye.
        var rgb = [0, 0, 0], samples = 0
        for y in 405..<415 { for x in 748..<758 {
            let p = data + y * stride + x * 4
            guard p[3] > 250 else { continue }
            rgb[0] += Int(p[0]); rgb[1] += Int(p[1]); rgb[2] += Int(p[2]); samples += 1
        } }
        let lid = samples == 0 ? CGColor(srgbRed: 0.63, green: 0.33, blue: 0.2, alpha: 1)
            : CGColor(srgbRed: CGFloat(rgb[0]) / CGFloat(samples * 255), green: CGFloat(rgb[1]) / CGFloat(samples * 255),
                      blue: CGFloat(rgb[2]) / CGFloat(samples * 255), alpha: 1)
        guard let image = ctx.makeImage() else { return nil }
        return (image, pupils, interiors, lid)
    }

    // Where a point on the sprite lands on the canvas (points, y-down) for a given bounce.
    static func canvasPoint(_ p: CGPoint, bounce: CGFloat) -> CGPoint {
        let scale = petWidth / source.width
        return CGPoint(x: (canvas.width - petWidth) / 2 + p.x * scale, y: 26 - bounce + p.y * scale)
    }

    static func bounce(phase: Double, walking: Bool) -> CGFloat { walking ? 2.2 * abs(sin(phase)) : 0 }

    func render(phase: Double, walking: Bool, pixelsPerPoint s: CGFloat, blankEyes: Bool = false) -> RenderedFrame? {
        let w = Int((Self.canvas.width * s).rounded()), h = Int((Self.canvas.height * s).rounded())
        guard let ctx = Self.flippedContext(width: w, height: h) else { return nil }
        ctx.scaleBy(x: s, y: s)
        let scale = Self.petWidth / Self.source.width
        let bounce = Self.bounce(phase: phase, walking: walking)
        ctx.translateBy(x: (Self.canvas.width - Self.petWidth) / 2, y: 26 - bounce)
        ctx.scaleBy(x: scale, y: scale)
        // Alternating planted and lifted hands propel the head forward.
        let swing = walking ? 18 * sin(phase) : 0
        drawArm(ctx, leftArm, pivot: CGPoint(x: 633, y: 808), degrees: -16 + swing)
        drawArm(ctx, rightArm, pivot: CGPoint(x: 1147, y: 808), degrees: 16 + swing)
        // Cover each rotating joint with a small patch of matching arm colour.
        ctx.setFillColor(red: 157 / 255, green: 87 / 255, blue: 47 / 255, alpha: 1)
        ctx.fillEllipse(in: CGRect(x: 612, y: 782, width: 60, height: 47))
        ctx.fillEllipse(in: CGRect(x: 1112, y: 782, width: 60, height: 47))
        Self.drawUpright(ctx, blankEyes ? blankEyedHead : head, in: CGRect(origin: .zero, size: Self.source))
        guard let image = ctx.makeImage() else { return nil }
        return RenderedFrame(image: image, opaqueBounds: Self.opaqueBounds(ctx, w, h, s))
    }

    private func drawArm(_ ctx: CGContext, _ arm: CGImage, pivot: CGPoint, degrees: Double) {
        ctx.saveGState()
        ctx.translateBy(x: pivot.x, y: pivot.y)
        ctx.rotate(by: degrees * .pi / 180)
        ctx.translateBy(x: -pivot.x, y: -pivot.y)
        Self.drawUpright(ctx, arm, in: CGRect(origin: .zero, size: Self.source))
        ctx.restoreGState()
    }

    // Bitmap memory is stored top row first, so rows here are already y-down.
    private static func opaqueBounds(_ ctx: CGContext, _ w: Int, _ h: Int, _ s: CGFloat) -> CGRect {
        guard let data = ctx.data?.assumingMemoryBound(to: UInt8.self) else { return CGRect(origin: .zero, size: canvas) }
        let stride = ctx.bytesPerRow
        var minX = w, minY = h, maxX = -1, maxY = -1
        for y in 0..<h {
            let row = data + y * stride
            for x in 0..<w where row[x * 4 + 3] > 8 {
                if x < minX { minX = x }
                if x > maxX { maxX = x }
                if y < minY { minY = y }
                maxY = y
            }
        }
        if maxX < 0 { return CGRect(origin: .zero, size: canvas) }
        return CGRect(x: CGFloat(minX) / s, y: CGFloat(minY) / s,
                      width: CGFloat(maxX - minX + 1) / s, height: CGFloat(maxY - minY + 1) / s)
    }
}

// MARK: - Settings

enum Key {
    static let zone = "zone", chase = "chase", speed = "speed"
    static let soundOn = "soundOn", soundGap = "soundGap", volume = "volume"
    static let size = "size", opacity = "opacity", layer = "layer", draggable = "draggable"
    static let watchCursor = "watchCursor", blink = "blink"
    static let count = "count"
    static let savedX = "savedX", savedY = "savedY"
}

private final class OptionTag: NSObject {
    let key: String, value: NSObject
    init(_ key: String, _ value: NSObject) { self.key = key; self.value = value }
}

// MARK: - Pet view

// Draws the current frame and, when dragging is enabled, lets you pick Onkey up.
final class PetView: NSView {
    var onDrag: ((NSPoint) -> Void)?
    var onDragEnd: (() -> Void)?
    var onClick: (() -> Void)?
    private var grabMouse = NSPoint.zero, grabOrigin = NSPoint.zero, moved = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.contentsGravity = .resize
    }
    required init?(coder: NSCoder) { fatalError() }

    func show(_ image: CGImage, scale: CGFloat) {
        layer?.contentsScale = scale
        layer?.contents = image
    }

    // The drawn pupils, as separate layers over blank eyes so they can follow the cursor.
    private var pupilLayers: [CALayer] = []

    // Centres are in view points (y-up); nil hides the pupils. pointsPerPixel converts
    // sprite pixels to view points at the current size.
    func showPupils(_ art: [Cutout], at centers: [CGPoint]?, pointsPerPixel k: CGFloat, contentsScale: CGFloat) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        defer { CATransaction.commit() }
        guard let centers else {
            pupilLayers.forEach { $0.isHidden = true }
            return
        }
        while pupilLayers.count < art.count {
            let pupil = CALayer()
            pupil.contentsGravity = .resize
            pupil.minificationFilter = .trilinear
            layer?.addSublayer(pupil)
            pupilLayers.append(pupil)
        }
        for (i, pupil) in pupilLayers.enumerated() where i < centers.count {
            let a = art[i], s = k * OnkeyRenderer.pupilScale
            pupil.isHidden = false
            pupil.contents = a.image
            pupil.contentsScale = contentsScale
            pupil.bounds = CGRect(x: 0, y: 0, width: a.rect.width * s, height: a.rect.height * s)
            // Pin the pupil's ink centre to the gaze point (layer y runs upward).
            pupil.anchorPoint = CGPoint(x: (a.center.x - a.rect.minX) / a.rect.width,
                                        y: (a.rect.maxY - a.center.y) / a.rect.height)
            pupil.position = centers[i]
        }
    }

    // One eyelid per eye, clipped to the inside of the eye outline so it never paints
    // over the outline or the muzzle. Lids sit above the pupils.
    private var lids: [(clip: CALayer, skin: CAShapeLayer, ink: CALayer, lash: CAShapeLayer, crease: CAShapeLayer)] = []

    // frames: each eye's cut-out rect in view points; closure: 0 open ... 1 shut.
    func showLids(_ interiors: [Cutout], frames: [CGRect], closure: [CGFloat], color: CGColor,
                  pointsPerPixel k: CGFloat) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        defer { CATransaction.commit() }
        while lids.count < interiors.count {
            let clip = CALayer(), mask = CALayer(), skin = CAShapeLayer()
            let ink = CALayer(), lash = CAShapeLayer(), crease = CAShapeLayer()
            mask.contents = interiors[lids.count].image
            mask.contentsGravity = .resize
            clip.mask = mask
            // Above the pupils, whichever was added first.
            clip.zPosition = 1
            ink.zPosition = 2
            let inkColor = CGColor(srgbRed: 0.16, green: 0.08, blue: 0.05, alpha: 1)
            lash.fillColor = inkColor
            crease.fillColor = inkColor
            clip.addSublayer(skin)
            // The ink lines sit outside the clip so their tapered ends can meet the outline.
            ink.addSublayer(crease)
            ink.addSublayer(lash)
            layer?.addSublayer(clip)
            layer?.addSublayer(ink)
            lids.append((clip, skin, ink, lash, crease))
        }
        for (i, lid) in lids.enumerated() {
            let c = i < closure.count ? closure[i] : 0
            lid.clip.isHidden = c <= 0.01
            lid.ink.isHidden = lid.clip.isHidden
            guard !lid.clip.isHidden, i < frames.count else { continue }
            let f = frames[i], w = f.width, h = f.height
            lid.clip.frame = f
            lid.ink.frame = f
            lid.clip.mask?.frame = CGRect(origin: .zero, size: f.size)
            lid.skin.fillColor = color
            // The lid's lower edge sags in the middle like a drawn eyelid. Layer y runs upward.
            let sag = h * 0.14
            let edgeY = h * (1 - c * 1.15) + sag * c
            let skin = CGMutablePath()
            skin.move(to: CGPoint(x: -2, y: h + 2))
            skin.addLine(to: CGPoint(x: w + 2, y: h + 2))
            skin.addLine(to: CGPoint(x: w + 2, y: edgeY))
            skin.addQuadCurve(to: CGPoint(x: -2, y: edgeY), control: CGPoint(x: w / 2, y: edgeY - 2 * sag))
            skin.closeSubpath()
            lid.skin.path = skin
            // The lash line follows the lid down, resting a little below the middle when shut.
            // It spans the eye's width at that height, overlapping the outline a touch.
            let lineY = max(edgeY, h * 0.44)
            let half = chord(at: lineY, w: w, h: h) + w * 0.03
            lid.lash.path = Self.inkStroke(from: CGPoint(x: w / 2 - half, y: lineY), to: CGPoint(x: w / 2 + half, y: lineY),
                                           sag: sag, width: 14 * k, wobble: 1.6 * k, seed: Double(i) * 3.1)
            // A faint crease above the shut lid.
            let creaseY = lineY + h * 0.2
            let creaseHalf = chord(at: creaseY, w: w, h: h) * 0.55
            lid.crease.opacity = Float(max(0, (c - 0.6) / 0.4)) * 0.55
            lid.crease.path = Self.inkStroke(from: CGPoint(x: w / 2 - creaseHalf, y: creaseY),
                                             to: CGPoint(x: w / 2 + creaseHalf, y: creaseY),
                                             sag: sag * 0.6, width: 5.5 * k, wobble: 0.8 * k, seed: Double(i) * 3.1 + 7)
        }
    }

    // The open mouth, drawn over the seam between his lips.
    private var mouth: (hole: CAShapeLayer, tongue: CAShapeLayer)?

    // open: 0 shut ... 1 fully open. spriteToView maps sprite pixels to view points.
    func showMouth(open: CGFloat, spriteToView: CGAffineTransform) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        defer { CATransaction.commit() }
        if mouth == nil {
            let hole = CAShapeLayer(), tongue = CAShapeLayer()
            hole.fillColor = CGColor(srgbRed: 0.05, green: 0.03, blue: 0.03, alpha: 1)
            tongue.fillColor = CGColor(srgbRed: 0.78, green: 0.16, blue: 0.16, alpha: 1)
            hole.zPosition = 3
            hole.addSublayer(tongue)
            layer?.addSublayer(hole)
            mouth = (hole, tongue)
        }
        guard let mouth else { return }
        mouth.hole.isHidden = open <= 0.02
        guard !mouth.hole.isHidden else { return }
        var t = spriteToView
        // Seam between the lips in Onkey.png: lowest at (900, 701), rising to the sides.
        let depth = 52 * open, width = 120 + 50 * open
        mouth.hole.path = Self.mouthShape(centerX: 900, top: 696, width: width, depth: depth, transform: &t)
        // A red tongue fills the top of the opening once there is room, as in the cartoon.
        mouth.tongue.isHidden = depth < 16
        mouth.tongue.path = Self.mouthShape(centerX: 900, top: 703, width: width - 26, depth: depth * 0.5,
                                            transform: &t)
    }

    // A flat-topped, round-bottomed opening in sprite pixels (y-down), whose top edge
    // follows the gentle sag of the lip seam.
    private static func mouthShape(centerX cx: CGFloat, top: CGFloat, width w: CGFloat, depth: CGFloat,
                                   transform t: inout CGAffineTransform) -> CGPath {
        let path = CGMutablePath()
        let left = CGPoint(x: cx - w / 2, y: top), right = CGPoint(x: cx + w / 2, y: top)
        let bottom = CGPoint(x: cx, y: top + 5 + depth)
        path.move(to: left, transform: t)
        path.addQuadCurve(to: right, control: CGPoint(x: cx, y: top + 10), transform: t)
        path.addCurve(to: bottom, control1: CGPoint(x: right.x, y: top + depth * 0.9),
                      control2: CGPoint(x: cx + w * 0.28, y: bottom.y), transform: t)
        path.addCurve(to: left, control1: CGPoint(x: cx - w * 0.28, y: bottom.y),
                      control2: CGPoint(x: left.x, y: top + depth * 0.9), transform: t)
        path.closeSubpath()
        return path
    }

    // Half the width of a round eye filling w x h, at height y.
    private func chord(at y: CGFloat, w: CGFloat, h: CGFloat) -> CGFloat {
        let r = min(w, h) / 2, dy = y - h / 2
        return abs(dy) >= r ? 0 : (r * r - dy * dy).squareRoot()
    }

    // A brush-like ink line along a sagging curve: thick in the middle, tapering to
    // fine points, with a fixed wobble so it looks hand-drawn rather than ruled.
    private static func inkStroke(from a: CGPoint, to b: CGPoint, sag: CGFloat, width: CGFloat,
                                  wobble: CGFloat, seed: Double) -> CGPath {
        let control = CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 - 2 * sag)
        let steps = 28
        var upper: [CGPoint] = [], lower: [CGPoint] = []
        for n in 0...steps {
            let t = CGFloat(n) / CGFloat(steps), u = 1 - t
            let p = CGPoint(x: u * u * a.x + 2 * u * t * control.x + t * t * b.x,
                            y: u * u * a.y + 2 * u * t * control.y + t * t * b.y)
            let d = CGPoint(x: 2 * u * (control.x - a.x) + 2 * t * (b.x - control.x),
                            y: 2 * u * (control.y - a.y) + 2 * t * (b.y - control.y))
            let len = max(0.0001, (d.x * d.x + d.y * d.y).squareRoot())
            let normal = CGPoint(x: -d.y / len, y: d.x / len)
            let td = Double(t)
            let jitter = wobble * CGFloat(sin(td * 11 + seed) * 0.6 + sin(td * 23 + seed * 1.7) * 0.4)
            let half = width / 2 * CGFloat(pow(sin(td * .pi), 0.7)) * CGFloat(0.85 + 0.15 * sin(td * 17 + seed))
            upper.append(CGPoint(x: p.x + normal.x * (jitter + half), y: p.y + normal.y * (jitter + half)))
            lower.append(CGPoint(x: p.x + normal.x * (jitter - half), y: p.y + normal.y * (jitter - half)))
        }
        let path = CGMutablePath()
        path.addLines(between: upper + lower.reversed())
        path.closeSubpath()
        return path
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) {
        grabMouse = NSEvent.mouseLocation
        grabOrigin = window?.frame.origin ?? .zero
        moved = false
    }
    override func mouseDragged(with event: NSEvent) {
        let now = NSEvent.mouseLocation
        let dx = now.x - grabMouse.x, dy = now.y - grabMouse.y
        if !moved && abs(dx) + abs(dy) < 3 { return }
        moved = true
        onDrag?(NSPoint(x: grabOrigin.x + dx, y: grabOrigin.y + dy))
    }
    override func mouseUp(with event: NSEvent) {
        if moved { onDragEnd?() } else { onClick?() }
    }
}

// MARK: - App

// One Onkey on screen: his window, where he's walking, and his eyes, blinks and voice.
// The drawing, settings and menu are shared through the app.
final class Pet {
    unowned let app: OnkeyApp
    let window: NSWindow
    let view: PetView
    var area: NSRect
    var px: Double, py: Double
    private var targetX = 0.0, targetY = 0.0, phase = 0.0
    private var restUntil = 0.0, nextSound = 0.0
    private(set) var dragging = false
    private var shown: CGImage?
    private var shownBounce: CGFloat = 0
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
    }

    // After a size change: keep him centred where he was, at the new size.
    func resize(from oldSize: NSSize) {
        let center = NSPoint(x: px + Double(oldSize.width) / 2, y: py + Double(oldSize.height) / 2)
        window.setContentSize(windowSize)
        view.frame = NSRect(origin: .zero, size: windowSize)
        px = center.x - windowSize.width / 2
        py = center.y - windowSize.height / 2
        clampToArea()
        pickTarget()
        present(app.idle)
    }

    func framesChanged() { shown = nil; present(app.idle) }

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
        let zone = app.defaults.string(forKey: Key.zone) ?? "anywhere"
        if zone == "stay" { targetX = px; targetY = py; return }
        var x = Double.random(in: l.minX...l.maxX), y = Double.random(in: l.minY...l.maxY)
        let chase = app.defaults.integer(forKey: Key.chase)
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
        if clock < restUntil || app.defaults.string(forKey: Key.zone) == "stay" { present(app.idle); return }
        let dx = targetX - px, dy = targetY - py
        let distance = (dx * dx + dy * dy).squareRoot()
        if distance < 3 {
            restUntil = clock + 2 + Double.random(in: 0...4)
            pickTarget()
            present(app.idle)
            return
        }
        let speed = app.defaults.double(forKey: Key.speed) * Double(size)
        let step = min(distance, speed * dt)
        px += step * dx / distance
        py += step * dy / distance
        // Faster walking means faster arms, so he never looks like he's skating.
        let strideRate: Double = 1.15 * speed / (42 * Double(size))
        phase = (phase + dt * 2 * Double.pi * strideRate).truncatingRemainder(dividingBy: 2 * Double.pi)
        let frames = app.frames
        let index = Int(phase / (2 * .pi) * Double(frames.count)) % frames.count
        present(frames[index], bounce: OnkeyRenderer.bounce(phase: Double(index) / Double(frames.count) * 2 * .pi,
                                                            walking: true))
    }

    func present(_ image: CGImage, bounce: CGFloat = 0) {
        if shown !== image { view.show(image, scale: app.pixelScale / size); shown = image }
        shownBounce = bounce
        window.setFrameOrigin(NSPoint(x: px.rounded(), y: py.rounded()))
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
        guard app.watching else { view.showPupils([], at: nil, pointsPerPixel: 0, contentsScale: 1); return }
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
        view.showPupils(app.renderer.pupils, at: centers, pointsPerPixel: pointsPerPixel,
                        contentsScale: 1 / pointsPerPixel)
    }

    // Blinks every 6-14 seconds: the eye on the left of the screen first, then the right
    // one following close behind, closing while the first is still shut. Each blink is a
    // quick close, a beat shut, and open (0.37 s in all).
    private static let blinkOrder: [Double] = [0, 0.14]   // Delay per eye: [left, right] on screen.

    private func updateLids(pointsPerPixel k: CGFloat) {
        let now = ProcessInfo.processInfo.systemUptime
        if app.defaults.bool(forKey: Key.blink) && now >= nextBlink {
            blinkStart = now
            nextBlink = now + 6 + Double.random(in: 0...8)
        }
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
    }

    // Opens his mouth "just a tad", following how loud the clip is at this moment.
    private func updateMouth(dt: Double) {
        let envelope = app.soundEnvelope
        let t = ProcessInfo.processInfo.systemUptime - soundStart
        // t is infinite until the first sound plays; only index the clip while it's playing.
        let playing = t >= 0 && t < Double(envelope.count) / 60
        let target = playing ? min(1, envelope[Int(t * 60)] * 1.25) : 0
        mouthOpen += (target - mouthOpen) * (1 - exp(-dt * 25))
        let s0 = OnkeyRenderer.canvasPoint(CGPoint(x: 1, y: 0), bounce: 0).x - OnkeyRenderer.canvasPoint(.zero, bounce: 0).x
        let origin = OnkeyRenderer.canvasPoint(.zero, bounce: shownBounce)
        // Sprite pixels (y-down) to view points (y-up), following his head's bounce.
        let map = CGAffineTransform(a: s0 * size, b: 0, c: 0, d: -s0 * size,
                                    tx: origin.x * size, ty: (OnkeyRenderer.canvas.height - origin.y) * size)
        view.showMouth(open: CGFloat(mouthOpen), spriteToView: map)
    }

    // MARK: Sound

    private func soundDelay() -> Double {
        let gap = app.defaults.double(forKey: Key.soundGap)
        return gap + Double.random(in: 0...gap)
    }

    func playSound(force: Bool) {
        guard let sound, force || app.defaults.bool(forKey: Key.soundOn) else { return }
        sound.volume = Float(app.defaults.double(forKey: Key.volume))
        sound.stop()
        sound.play()
        soundStart = ProcessInfo.processInfo.systemUptime
    }

    func stopSound() {
        sound?.stop()
        soundStart = -.infinity
    }
}

final class OnkeyApp: NSObject, NSApplicationDelegate {
    let defaults = UserDefaults.standard
    private(set) var renderer: OnkeyRenderer!
    private var statusItem: NSStatusItem!
    private var pauseItem: NSMenuItem!
    private var loginItem: NSMenuItem!
    private var optionItems: [NSMenuItem] = []
    private var soundOptionItems: [NSMenuItem] = []
    private var pets: [Pet] = []
    private(set) var frames: [CGImage] = []
    private(set) var idle: CGImage!
    private(set) var petBounds = CGRect(origin: .zero, size: OnkeyRenderer.canvas)  // Points, y-down, unscaled.
    private(set) var pixelScale: CGFloat = 2
    private(set) var baseSound: NSSound?
    // Loudness of the sound clip, 60 samples per second, scaled 0...1, to move his mouth.
    private(set) var soundEnvelope: [Double] = []
    private var timer: Timer?
    // Seconds Onkey has been awake; stops while paused.
    private(set) var clock = 0.0
    private var lastTick = 0.0
    private var paused = false
    var watching: Bool { defaults.bool(forKey: Key.watchCursor) }

    var size: CGFloat { CGFloat(defaults.double(forKey: Key.size)) }
    var windowSize: NSSize {
        NSSize(width: OnkeyRenderer.canvas.width * size, height: OnkeyRenderer.canvas.height * size)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        defaults.register(defaults: [
            Key.zone: "anywhere", Key.chase: 5, Key.speed: 42.0,
            Key.soundOn: true, Key.soundGap: 90.0, Key.volume: 1.0,
            Key.size: 1.0, Key.opacity: 1.0, Key.layer: "above", Key.draggable: false, Key.watchCursor: true, Key.blink: true,
            Key.count: 1,
        ])
        let folder = Self.assetFolder()
        guard let r = OnkeyRenderer(spriteURL: folder.appendingPathComponent("Onkey.png")) else {
            fail("Could not load Onkey.png from \(folder.path).")
            return
        }
        renderer = r
        let soundURL = folder.appendingPathComponent("Sounds/oooo.wav")
        baseSound = NSSound(contentsOf: soundURL, byReference: false)
        soundEnvelope = Self.loudness(of: soundURL)
        renderFrames()

        // The first Onkey comes back where he was last time.
        var area = Self.screenUnderMouse().visibleFrame
        var origin = NSPoint(x: area.maxX - windowSize.width - 60, y: area.minY + 40)
        if defaults.object(forKey: Key.savedX) != nil {
            let saved = NSPoint(x: defaults.double(forKey: Key.savedX), y: defaults.double(forKey: Key.savedY))
            if let screen = NSScreen.screens.first(where: { $0.visibleFrame.insetBy(dx: -200, dy: -200).contains(saved) }) {
                area = screen.visibleFrame
                origin = saved
            }
        }
        addPet(at: origin, in: area)
        matchPetCount()
        buildMenu()

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in self?.screensChanged() }

        lastTick = ProcessInfo.processInfo.systemUptime
        let t = Timer(timeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func applicationWillTerminate(_ notification: Notification) { savePosition() }

    private static func assetFolder() -> URL {
        if let env = ProcessInfo.processInfo.environment["ONKEY_ASSET_DIR"] {
            return URL(fileURLWithPath: env)
        }
        return Bundle.main.resourceURL ?? URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
    }

    static func screenUnderMouse() -> NSScreen {
        let mouse = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens[0]
    }

    // MARK: Onkeys

    private func addPet(at origin: NSPoint, in area: NSRect) {
        let pet = Pet(app: self, origin: origin, area: area)
        if pets.isEmpty { pet.onMoved = { [weak self] in self?.savePosition() } }
        pet.backingObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.didChangeBackingPropertiesNotification, object: pet.window, queue: .main) { [weak self] _ in
            guard let self else { return }
            self.renderFrames()
            self.pets.forEach { $0.framesChanged() }
        }
        pets.append(pet)
    }

    // Adds or removes Onkeys to match the "How Many Onkeys" setting. New ones turn up
    // somewhere random on the first Onkey's screen.
    private func matchPetCount() {
        let wanted = max(1, defaults.integer(forKey: Key.count))
        while pets.count > wanted {
            let pet = pets.removeLast()
            if let observer = pet.backingObserver { NotificationCenter.default.removeObserver(observer) }
            pet.close()
        }
        while pets.count < wanted {
            let area = pets.first?.area ?? Self.screenUnderMouse().visibleFrame
            let origin = NSPoint(x: Double.random(in: area.minX...max(area.minX, area.maxX - windowSize.width)),
                                 y: Double.random(in: area.minY...max(area.minY, area.maxY - windowSize.height)))
            addPet(at: origin, in: area)
        }
    }

    // MARK: Frames

    private func renderFrames() {
        pixelScale = size * (pets.first?.window.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2)
        var bounds = CGRect.null
        var rendered: [CGImage] = []
        for i in 0..<OnkeyRenderer.frameCount {
            let phase = Double(i) / Double(OnkeyRenderer.frameCount) * 2 * .pi
            guard let f = renderer.render(phase: phase, walking: true, pixelsPerPoint: pixelScale,
                                          blankEyes: watching) else { continue }
            rendered.append(f.image)
            bounds = bounds.union(f.opaqueBounds)
        }
        guard let still = renderer.render(phase: 0, walking: false, pixelsPerPoint: pixelScale,
                                          blankEyes: watching) else { return }
        idle = still.image
        frames = rendered.isEmpty ? [still.image] : rendered
        petBounds = bounds.union(still.opaqueBounds)
    }

    private func menuIcon() -> NSImage {
        guard let f = renderer.render(phase: 0, walking: false, pixelsPerPoint: 4),
              let cropped = f.image.cropping(to: f.opaqueBounds.applying(CGAffineTransform(scaleX: 4, y: 4)).integral)
        else { return NSImage() }
        let height: CGFloat = 18
        let width = height * CGFloat(cropped.width) / CGFloat(cropped.height)
        return NSImage(cgImage: cropped, size: NSSize(width: width, height: height))
    }

    // MARK: Menu

    private func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = menuIcon()
        statusItem.button?.toolTip = "Onkey"

        let menu = NSMenu()
        pauseItem = item("Pause Onkey", #selector(togglePause), "p")
        menu.addItem(pauseItem)
        menu.addItem(.separator())

        menu.addItem(submenu("How Many Onkeys", [
            option("One", Key.count, 1),
            option("Two", Key.count, 2),
            option("Three", Key.count, 3),
            option("Five", Key.count, 5),
            option("Ten (chaos)", Key.count, 10),
        ]))

        menu.addItem(submenu("Where Onkey Goes", [
            option("Anywhere on the screen", Key.zone, "anywhere"),
            option("Along the bottom", Key.zone, "bottom"),
            option("Along the top", Key.zone, "top"),
            option("Up and down the left side", Key.zone, "left"),
            option("Up and down the right side", Key.zone, "right"),
            option("Stay in one spot", Key.zone, "stay"),
            .separator(),
            header("Walks toward my mouse"),
            option("Never", Key.chase, 0),
            option("Sometimes", Key.chase, 5),
            option("Often", Key.chase, 2),
            option("Always", Key.chase, 1),
            .separator(),
            header("Walking speed"),
            option("Slow", Key.speed, 22.0),
            option("Normal", Key.speed, 42.0),
            option("Fast", Key.speed, 80.0),
            option("Zoomies", Key.speed, 160.0),
            .separator(),
            item("Bring Onkey to This Screen", #selector(bringHere), "b"),
        ]))

        let playNow = item("Play Sound Now", #selector(playNow), "s")
        let soundOptions = [
            header("How often"),
            option("Every 20-40 seconds", Key.soundGap, 20.0),
            option("Every 1½-3 minutes", Key.soundGap, 90.0),
            option("Every 5-10 minutes", Key.soundGap, 300.0),
            .separator(),
            header("Volume"),
            option("Quiet", Key.volume, 0.25),
            option("Medium", Key.volume, 0.6),
            option("Loud", Key.volume, 1.0),
        ]
        soundOptionItems = soundOptions + [playNow]
        menu.addItem(submenu("Sound", [option("Sound On", Key.soundOn, true), .separator()]
                             + soundOptions + [.separator(), playNow]))

        menu.addItem(submenu("Appearance", [
            header("Size"),
            option("Tiny", Key.size, 0.5),
            option("Small", Key.size, 0.75),
            option("Normal", Key.size, 1.0),
            option("Large", Key.size, 1.5),
            option("Huge", Key.size, 2.25),
            .separator(),
            header("Opacity"),
            option("Solid", Key.opacity, 1.0),
            option("See-through", Key.opacity, 0.7),
            option("Ghost", Key.opacity, 0.35),
            .separator(),
            header("Layer"),
            option("In front of all windows", Key.layer, "above"),
            option("On the desktop, behind windows", Key.layer, "desktop"),
            .separator(),
            header("Eyes"),
            option("Watch my cursor", Key.watchCursor, true),
            option("Blink now and then", Key.blink, true),
        ]))

        menu.addItem(option("Let Me Drag Onkey Around", Key.draggable, true))
        menu.addItem(.separator())
        loginItem = item("Open Onkey at Login", #selector(toggleLogin), "")
        menu.addItem(loginItem)
        menu.addItem(item("Quit Onkey", #selector(quit), "q"))
        statusItem.menu = menu
        refreshMenu()
    }

    private func item(_ title: String, _ action: Selector, _ key: String) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: key)
        i.target = self
        return i
    }

    private func header(_ title: String) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        i.isEnabled = false
        return i
    }

    private func submenu(_ title: String, _ items: [NSMenuItem]) -> NSMenuItem {
        let parent = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let sub = NSMenu(title: title)
        sub.autoenablesItems = false
        items.forEach { sub.addItem($0) }
        parent.submenu = sub
        return parent
    }

    // A menu choice stored in UserDefaults. Bool options toggle; others act as radio buttons.
    private func option(_ title: String, _ key: String, _ value: Any) -> NSMenuItem {
        let i = item(title, #selector(chooseOption(_:)), "")
        i.representedObject = OptionTag(key, value as! NSObject)
        optionItems.append(i)
        return i
    }

    @objc private func chooseOption(_ sender: NSMenuItem) {
        guard let tag = sender.representedObject as? OptionTag else { return }
        let oldWindowSize = windowSize
        if CFGetTypeID(tag.value) == CFBooleanGetTypeID() {
            defaults.set(!defaults.bool(forKey: tag.key), forKey: tag.key)
        } else {
            defaults.set(tag.value, forKey: tag.key)
        }
        switch tag.key {
        case Key.count:
            matchPetCount()
        case Key.size:
            renderFrames()
            pets.forEach { $0.resize(from: oldWindowSize) }
        case Key.watchCursor:
            renderFrames()
            pets.forEach { $0.framesChanged(); $0.updateFace(dt: 1) }
        case Key.opacity, Key.layer, Key.draggable:
            pets.forEach { $0.applyAppearance() }
        case Key.zone, Key.chase:
            pets.forEach { $0.restFor(0); $0.pickTarget() }
        case Key.soundGap:
            pets.forEach { $0.rescheduleSound() }
        case Key.soundOn:
            if !defaults.bool(forKey: Key.soundOn) { pets.forEach { $0.stopSound() } }
        default:
            break
        }
        refreshMenu()
    }

    private func refreshMenu() {
        for i in optionItems {
            guard let tag = i.representedObject as? OptionTag else { continue }
            i.state = (defaults.object(forKey: tag.key) as? NSObject)?.isEqual(tag.value) == true ? .on : .off
        }
        let soundOn = defaults.bool(forKey: Key.soundOn) && baseSound != nil
        soundOptionItems.forEach { if $0.action != nil { $0.isEnabled = soundOn } }
        if #available(macOS 13, *) {
            loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
        } else {
            loginItem.isHidden = true
        }
    }

    // MARK: Ticking

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        let dt = min(0.08, max(0, now - lastTick))
        lastTick = now
        if !paused { clock += dt }
        for pet in pets {
            if !paused && !pet.dragging { pet.walk(dt: dt) }
            pet.updateFace(dt: dt)
        }
    }

    private func screensChanged() {
        for pet in pets {
            pet.area = (pet.window.screen ?? Self.screenUnderMouse()).visibleFrame
            pet.clampToArea()
            pet.pickTarget()
            pet.present(idle)
        }
    }

    private func savePosition() {
        guard let lead = pets.first else { return }
        defaults.set(lead.px, forKey: Key.savedX)
        defaults.set(lead.py, forKey: Key.savedY)
    }

    private static func loudness(of url: URL) -> [Double] {
        guard let file = try? AVAudioFile(forReading: url),
              let buffer = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: AVAudioFrameCount(file.length)),
              (try? file.read(into: buffer)) != nil,
              let samples = buffer.floatChannelData?[0] else { return [] }
        let window = max(1, Int(file.processingFormat.sampleRate / 60))
        var levels: [Double] = []
        var start = 0
        while start < Int(buffer.frameLength) {
            let end = min(Int(buffer.frameLength), start + window)
            var sum = 0.0
            for i in start..<end { sum += Double(samples[i] * samples[i]) }
            levels.append((sum / Double(end - start)).squareRoot())
            start = end
        }
        let loudest = levels.max() ?? 0
        return loudest > 0 ? levels.map { $0 / loudest } : []
    }

    // MARK: Actions

    @objc private func togglePause() {
        paused.toggle()
        pauseItem.title = paused ? "Resume Onkey" : "Pause Onkey"
        if paused { pets.forEach { $0.stopSound(); $0.present(idle) } }
    }

    // Every Onkey says it at once.
    @objc private func playNow() { pets.forEach { $0.playSound(force: true) } }

    // Gathers every Onkey onto the screen under the mouse, loosely around the middle.
    @objc private func bringHere() {
        let area = Self.screenUnderMouse().visibleFrame
        for (i, pet) in pets.enumerated() {
            let spread = i == 0 ? 0 : min(area.width, area.height) * 0.25
            pet.area = area
            pet.px = area.midX - windowSize.width / 2 + Double.random(in: -spread...max(-spread, spread))
            pet.py = area.midY - windowSize.height / 2 + Double.random(in: -spread...max(-spread, spread))
            pet.clampToArea()
            pet.restFor(2)
            pet.pickTarget()
            pet.present(idle)
        }
        savePosition()
    }

    @objc private func toggleLogin() {
        guard #available(macOS 13, *) else { return }
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            let alert = NSAlert()
            alert.messageText = "Couldn't change the login setting"
            alert.informativeText = "\(error.localizedDescription)\n\nYou can add Onkey yourself in System Settings > General > Login Items."
            NSApp.activate(ignoringOtherApps: true)
            alert.runModal()
        }
        refreshMenu()
    }

    @objc private func quit() { NSApp.terminate(nil) }

    private func fail(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "Onkey could not start"
        alert.informativeText = message
        alert.runModal()
        NSApp.terminate(nil)
    }
}

// `Onkey --export <folder>` writes the rendered frames as PNGs, for checking the renderer.
if let i = CommandLine.arguments.firstIndex(of: "--export"), i + 1 < CommandLine.arguments.count {
    let out = URL(fileURLWithPath: CommandLine.arguments[i + 1])
    let sprite = URL(fileURLWithPath: CommandLine.arguments.count > i + 2 ? CommandLine.arguments[i + 2] : "Onkey.png")
    guard let r = OnkeyRenderer(spriteURL: sprite) else { print("Could not load \(sprite.path)"); exit(1) }
    try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
    func write(_ f: RenderedFrame?, _ name: String) {
        guard let f, let dest = CGImageDestinationCreateWithURL(out.appendingPathComponent(name) as CFURL,
                                                                "public.png" as CFString, 1, nil) else { return }
        CGImageDestinationAddImage(dest, f.image, nil)
        CGImageDestinationFinalize(dest)
    }
    write(r.render(phase: 0, walking: false, pixelsPerPoint: 1), "idle.png")
    write(r.render(phase: 0, walking: false, pixelsPerPoint: 4, blankEyes: true), "idle-blank-eyes@4x.png")
    // Blink test: the pet view composited offscreen at 4x, eyes half shut then shut.
    let k4 = 4 * (OnkeyRenderer.canvasPoint(CGPoint(x: 1, y: 0), bounce: 0).x - OnkeyRenderer.canvasPoint(.zero, bounce: 0).x)
    for (name, closure) in [("blink-half", [0.5, 0.5]), ("blink-shut", [1.0, 1.0]), ("blink-right-only", [0.0, 1.0])] {
        let view = PetView(frame: NSRect(x: 0, y: 0, width: OnkeyRenderer.canvas.width * 4, height: OnkeyRenderer.canvas.height * 4))
        view.show(r.render(phase: 0, walking: false, pixelsPerPoint: 4, blankEyes: true)!.image, scale: 1)
        view.showPupils(r.pupils, at: r.pupils.map { p in
            let c = OnkeyRenderer.canvasPoint(p.center, bounce: 0)
            return CGPoint(x: c.x * 4, y: (OnkeyRenderer.canvas.height - c.y) * 4)
        }, pointsPerPixel: k4, contentsScale: 1 / k4)
        let frames = r.eyeInteriors.map { e -> CGRect in
            let tl = OnkeyRenderer.canvasPoint(CGPoint(x: e.rect.minX, y: e.rect.minY), bounce: 0)
            return CGRect(x: tl.x * 4, y: (OnkeyRenderer.canvas.height - tl.y) * 4 - e.rect.height * k4,
                          width: e.rect.width * k4, height: e.rect.height * k4)
        }
        view.showLids(r.eyeInteriors, frames: frames, closure: closure.map { CGFloat($0) }, color: r.lidColor, pointsPerPixel: k4)
        let size = view.bounds.size
        let ctx = CGContext(data: nil, width: Int(size.width), height: Int(size.height), bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        view.layer!.render(in: ctx)
        write(RenderedFrame(image: ctx.makeImage()!, opaqueBounds: .zero), "\(name).png")
    }
    for (name, open) in [("mouth-half", 0.5), ("mouth-open", 1.0)] {
        let view = PetView(frame: NSRect(x: 0, y: 0, width: OnkeyRenderer.canvas.width * 4, height: OnkeyRenderer.canvas.height * 4))
        view.show(r.render(phase: 0, walking: false, pixelsPerPoint: 4)!.image, scale: 1)
        let s0 = k4 / 4, o = OnkeyRenderer.canvasPoint(.zero, bounce: 0)
        view.showMouth(open: open, spriteToView: CGAffineTransform(a: s0 * 4, b: 0, c: 0, d: -s0 * 4, tx: o.x * 4,
                                                                    ty: (OnkeyRenderer.canvas.height - o.y) * 4))
        let size = view.bounds.size
        let ctx = CGContext(data: nil, width: Int(size.width), height: Int(size.height), bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        view.layer!.render(in: ctx)
        write(RenderedFrame(image: ctx.makeImage()!, opaqueBounds: .zero), "\(name).png")
    }
    for (n, pupil) in r.pupils.enumerated() {
        write(RenderedFrame(image: pupil.image, opaqueBounds: pupil.rect), "pupil\(n).png")
        print("pupil\(n) rect \(pupil.rect) center \(pupil.center)")
    }
    for n in 0..<OnkeyRenderer.frameCount {
        write(r.render(phase: Double(n) / 40 * 2 * .pi, walking: true, pixelsPerPoint: 1), String(format: "frame%02d.png", n))
    }
    exit(0)
}

let app = NSApplication.shared
let delegate = OnkeyApp()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
