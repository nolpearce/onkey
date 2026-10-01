import CoreGraphics
import Foundation
import ImageIO

// Port of OnkeyRenderer from Windows/Source/Renderer.cs. Arm masks separate the arms from the ears
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
