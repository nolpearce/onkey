import AppKit

// Draws the current frame and, when dragging is enabled, lets you pick Onkey up.
final class PetView: NSView {
    var onDrag: ((NSPoint) -> Void)?
    var onDragEnd: (() -> Void)?
    var onClick: (() -> Void)?
    private var grabMouse = NSPoint.zero, grabOrigin = NSPoint.zero, moved = false
    // Everything Onkey is drawn with (body, pupils, lids, mouth), so a bop squashes it all.
    private let content = CALayer()
    private var baseY: CGFloat = 0

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        content.contentsGravity = .resize
        layer?.addSublayer(content)
        layout()
    }
    required init?(coder: NSCoder) { fatalError() }

    override func layout() {
        super.layout()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        content.transform = CATransform3DIdentity
        content.frame = bounds
        setBase(baseY)
        CATransaction.commit()
    }

    func show(_ image: CGImage, scale: CGFloat) {
        content.contentsScale = scale
        content.contents = image
    }

    // Where his base is (view points up from the bottom): squashing pivots there, so his
    // hands stay planted and the top of his head comes down.
    func setBase(_ y: CGFloat) {
        baseY = y
        let transform = content.transform
        content.transform = CATransform3DIdentity
        content.anchorPoint = CGPoint(x: 0.5, y: bounds.height > 0 ? y / bounds.height : 0)
        content.frame = bounds
        content.transform = transform
    }

    // squash: 0 normal ... 0.15 is 15% shorter.
    func setSquash(_ squash: CGFloat) {
        content.transform = CATransform3DMakeScale(1, 1 - squash, 1)
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
            content.addSublayer(pupil)
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
            content.addSublayer(clip)
            content.addSublayer(ink)
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
            content.addSublayer(hole)
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
