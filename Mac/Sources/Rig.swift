import CoreGraphics
import Foundation

// Onkey's arms as a rig: each one is a soft noodle from his shoulder to his wrist, with the
// hand from the drawing on the end. Everything here is in sprite pixels (y-down), and
// Windows/Source/Rig.cs mirrors it line for line so both look the same.

// Where an arm sits in Onkey.png and how thick it's drawn there, measured from the sprite.
struct ArmSpec {
    let out: Double                    // -1 for his left arm (reaching left), +1 for the right.
    let root: CGPoint                  // Inside the body, where the arm starts.
    let cut: Double                    // Distance from the root to the body's edge.
    let wrist: CGPoint                 // Where the hand from the drawing joins the arm.
    let bottom: Double, top: Double    // Half thickness below and above the middle of the arm.
    let flare: (size: Double, at: Double, fade: Double)   // The armpit curve into the body.
    let endFlare: (size: Double, fade: Double)            // The arm widening into the hand.
    let mark: (at: Double, offset: Double)                // The little hair mark.
    let handBox: CGRect                // The hand in Onkey.png.

    var length: Double { abs(Double(wrist.x - root.x)) }
    var restAngle: Double { out > 0 ? 0 : .pi }

    static let left = ArmSpec(out: -1, root: CGPoint(x: 640, y: 804.5), cut: 34, wrist: CGPoint(x: 250, y: 804.5),
                              bottom: 31.5, top: 31.5, flare: (29, 34, 55), endFlare: (3, 18), mark: (42, -4.5),
                              handBox: CGRect(x: 0, y: 600, width: 262, height: 287))
    static let right = ArmSpec(out: 1, root: CGPoint(x: 1134, y: 804), cut: 16, wrist: CGPoint(x: 1530, y: 804),
                               bottom: 29, top: 29, flare: (19, 26, 45), endFlare: (6, 18), mark: (363, -7),
                               handBox: CGRect(x: 1518, y: 600, width: 256, height: 287))
    static let both = [left, right]

    // What's cut out of the drawing to make room for each arm (the rest is his body).
    static let leftCut: [CGPoint] = [CGPoint(x: 0, y: 600), CGPoint(x: 380, y: 600), CGPoint(x: 380, y: 703),
                                     CGPoint(x: 606, y: 703), CGPoint(x: 606, y: 887), CGPoint(x: 0, y: 887)]
    static let rightCut: [CGPoint] = [CGPoint(x: 1774, y: 600), CGPoint(x: 1394, y: 600), CGPoint(x: 1394, y: 713),
                                      CGPoint(x: 1150, y: 713), CGPoint(x: 1150, y: 887), CGPoint(x: 1774, y: 887)]
}

// Where the wrist is and which way the hand points (radians, y-down).
struct ArmPose: Equatable {
    var wrist: CGPoint
    var angle: Double

    static func rest(_ spec: ArmSpec) -> ArmPose { ArmPose(wrist: spec.wrist, angle: spec.restAngle) }

    func mix(_ other: ArmPose, _ t: Double) -> ArmPose {
        var turn = (other.angle - angle).truncatingRemainder(dividingBy: 2 * .pi)
        if turn > .pi { turn -= 2 * .pi } else if turn < -.pi { turn += 2 * .pi }
        return ArmPose(wrist: CGPoint(x: Double(wrist.x) + Double(other.wrist.x - wrist.x) * t,
                                     y: Double(wrist.y) + Double(other.wrist.y - wrist.y) * t),
                       angle: angle + turn * t)
    }
}

// An arm ready to draw: the brown inside, its two ink outlines, and the hair mark.
struct ArmShape {
    var fill: [CGPoint] = []
    var ink: [[CGPoint]] = []
    var markFrom = CGPoint.zero, markTo = CGPoint.zero
}

enum ArmGeometry {
    static let ink = 5.0       // Outline width in the drawing.
    static let samples = 40

    // The arch a too-long arm makes: flat where it leaves the body, rounder toward the hand.
    private static func bump(_ t: Double) -> Double {
        let s = sin(Double.pi * t)
        return t < 0.5 ? s * s : pow(max(0, s), 1.4)
    }

    private static func length(_ p: [CGPoint]) -> Double {
        var total = 0.0
        for i in 1..<p.count { total += hypot(Double(p[i].x - p[i - 1].x), Double(p[i].y - p[i - 1].y)) }
        return total
    }

    // The middle of the arm from root to wrist. It leaves the body level and arrives along the
    // hand. Short of room, it arches like an elbow; with too much, it stretches thinner.
    static func centerline(_ spec: ArmSpec, root r: CGPoint, pose: ArmPose) -> (points: [CGPoint], thin: Double) {
        let w = pose.wrist
        let hx = cos(pose.angle), hy = sin(pose.angle)
        let chord = max(1e-6, hypot(Double(w.x - r.x), Double(w.y - r.y)))
        let p1 = (x: Double(r.x) + spec.out * chord * 0.4, y: Double(r.y))
        let p2 = (x: Double(w.x) - hx * chord * 0.3, y: Double(w.y) - hy * chord * 0.3)
        var base: [CGPoint] = []
        for i in 0...samples {
            let t = Double(i) / Double(samples), u = 1 - t
            let a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t
            base.append(CGPoint(x: a * Double(r.x) + b * p1.x + c * p2.x + d * Double(w.x),
                                y: a * Double(r.y) + b * p1.y + c * p2.y + d * Double(w.y)))
        }
        let along = length(base)
        // His top side: up when he's lying flat, outward when he dangles.
        let cx = Double(w.x - r.x) / chord, cy = Double(w.y - r.y) / chord
        let nx = spec.out < 0 ? -cy : cy, ny = spec.out < 0 ? cx : -cx
        func arched(_ amount: Double) -> [CGPoint] {
            base.enumerated().map { i, p -> CGPoint in
                let k = amount * bump(Double(i) / Double(samples))
                return CGPoint(x: Double(p.x) + nx * k, y: Double(p.y) + ny * k)
            }
        }
        if along >= spec.length { return (base, max(0.72, (spec.length / along).squareRoot())) }
        var lo = 0.0, hi = spec.length
        for _ in 0..<16 {
            let mid = (lo + hi) / 2
            if length(arched(mid)) < spec.length { lo = mid } else { hi = mid }
        }
        return (arched(lo), 1)
    }

    // Outlines the arm. boil (sprite pixels) makes the lines wander a little, differently for
    // each seed, like a drawing redrawn every few frames.
    static func shape(_ spec: ArmSpec, root: CGPoint, pose: ArmPose, seed: Double, boil: Double) -> ArmShape {
        let (p, thin) = centerline(spec, root: root, pose: pose)
        let n = p.count
        var s = [0.0]
        for i in 1..<n { s.append(s[i - 1] + hypot(Double(p[i].x - p[i - 1].x), Double(p[i].y - p[i - 1].y))) }
        let total = s[n - 1]
        var tops: [CGPoint] = [], bottoms: [CGPoint] = []
        var topOuter: [CGPoint] = [], topInner: [CGPoint] = [], bottomOuter: [CGPoint] = [], bottomInner: [CGPoint] = []
        var normals: [(Double, Double, Double, Double)] = []
        for i in 0..<n {
            let a = p[max(0, i - 1)], b = p[min(n - 1, i + 1)]
            var tx = Double(b.x - a.x), ty = Double(b.y - a.y)
            let l = max(1e-6, hypot(tx, ty)); tx /= l; ty /= l
            let nx = spec.out < 0 ? -ty : ty, ny = spec.out < 0 ? tx : -tx
            normals.append((tx, ty, nx, ny))
            let t = Double(i) / Double(n - 1)
            let squeeze = 1 - (1 - thin) * pow(sin(Double.pi * t), 2)
            let top = spec.top * squeeze + spec.flare.size * exp(-(s[i] - spec.flare.at) / spec.flare.fade)
                + spec.endFlare.size * exp(-(total - s[i]) / spec.endFlare.fade)
            let bottom = spec.bottom * squeeze
            // The lines hold still where they meet the body and the hand.
            let fade = min(1, max(0, (s[i] - spec.cut) / 40), max(0, (total - s[i]) / 40))
            let j1 = boil * fade * (0.6 * sin(s[i] / 23 + seed) + 0.4 * sin(s[i] / 9.7 + seed * 1.7))
            let j2 = boil * fade * (0.6 * sin(s[i] / 19 + seed * 2.3) + 0.4 * sin(s[i] / 11.3 + seed * 0.7))
            let wobble = boil > 0 ? 0.12 * fade : 0
            let wt = ink * (1 + wobble * sin(s[i] / 14 + seed * 3.1)), wb = ink * (1 + wobble * sin(s[i] / 13 + seed * 1.3))
            let ct = top - ink / 2 + j1, cb = bottom - ink / 2 + j2
            func at(_ k: Double) -> CGPoint { CGPoint(x: Double(p[i].x) + nx * k, y: Double(p[i].y) + ny * k) }
            tops.append(at(ct)); bottoms.append(at(-cb))
            topOuter.append(at(ct + wt / 2)); topInner.append(at(ct - wt / 2))
            bottomOuter.append(at(-cb - wb / 2)); bottomInner.append(at(-cb + wb / 2))
        }
        // Each edge runs on a little under the hand so no gap opens at the wrist.
        let (ex, ey, _, _) = normals[n - 1]
        func onward(_ q: CGPoint) -> CGPoint { CGPoint(x: Double(q.x) + ex * 12, y: Double(q.y) + ey * 12) }
        tops.append(onward(tops[n - 1])); bottoms.append(onward(bottoms[n - 1]))
        topOuter.append(onward(topOuter[n - 1])); topInner.append(onward(topInner[n - 1]))
        bottomOuter.append(onward(bottomOuter[n - 1])); bottomInner.append(onward(bottomInner[n - 1]))
        var shape = ArmShape()
        shape.fill = tops + bottoms.reversed()
        shape.ink = [topOuter + topInner.reversed(), bottomOuter + bottomInner.reversed()]
        // The hair mark: a short slanted dash.
        let i = min(n - 2, s.firstIndex(where: { $0 >= spec.mark.at }) ?? n - 2)
        let (tx, ty, nx, ny) = normals[i]
        let c = CGPoint(x: Double(p[i].x) - nx * spec.mark.offset, y: Double(p[i].y) - ny * spec.mark.offset)
        let mx = (tx * 0.5 + nx * 0.85) * 4, my = (ty * 0.5 + ny * 0.85) * 4
        shape.markFrom = CGPoint(x: Double(c.x) - mx, y: Double(c.y) - my)
        shape.markTo = CGPoint(x: Double(c.x) + mx, y: Double(c.y) + my)
        return shape
    }
}

// An arm hanging loose while he's carried: the wrist and the tip of the hand are two
// weights on a stretchy arm, swinging as the window moves. Positions are in "world" sprite
// pixels, so the window moving under them is what sets them swinging.
private struct Dangle {
    static let gravity = 15000.0   // Sprite pixels per second², for a swing of about a second.
    static let hand = 200.0        // Wrist to fingertips.
    // The lowest the fingertips can go, below the canvas's top (sprite pixels).
    static let bottom = Double(OnkeyRenderer.canvas.height - 26) / OnkeyRenderer.spriteScale - 90
    var wrist = CGPoint.zero, wristBefore = CGPoint.zero
    var tip = CGPoint.zero, tipBefore = CGPoint.zero

    init(spec: ArmSpec, origin: CGPoint, pose: ArmPose) {
        wrist = CGPoint(x: origin.x + pose.wrist.x, y: origin.y + pose.wrist.y)
        tip = CGPoint(x: Double(wrist.x) + cos(pose.angle) * Self.hand, y: Double(wrist.y) + sin(pose.angle) * Self.hand)
        wristBefore = wrist; tipBefore = tip
    }

    mutating func step(spec: ArmSpec, origin: CGPoint, dt: Double) {
        let root = CGPoint(x: origin.x + spec.root.x, y: origin.y + spec.root.y)
        func fall(_ p: inout CGPoint, _ before: inout CGPoint) {
            var vx = Double(p.x - before.x) * 0.985, vy = Double(p.y - before.y) * 0.985
            let v = hypot(vx, vy)
            if v > 700 { vx *= 700 / v; vy *= 700 / v }   // A hard fling mustn't send them flying apart.
            before = p
            p = CGPoint(x: Double(p.x) + vx, y: Double(p.y) + vy + Self.gravity * dt * dt)
        }
        fall(&wrist, &wristBefore)
        fall(&tip, &tipBefore)
        for _ in 0..<4 {
            // The arm stretches a little but never folds right up, and his hands don't cross.
            let limit = Double(origin.x) + 887 + spec.out * 110
            if spec.out < 0 ? Double(wrist.x) > limit : Double(wrist.x) < limit { wrist.x = CGFloat(limit) }
            // And they stay inside his window.
            wrist.y = min(wrist.y, origin.y + CGFloat(Self.bottom - Self.hand))
            tip.y = min(tip.y, origin.y + CGFloat(Self.bottom))
            let dx = Double(wrist.x - root.x), dy = Double(wrist.y - root.y)
            let d = max(1e-6, hypot(dx, dy))
            let reach = min(max(d, 0.75 * spec.length), 1.08 * spec.length)
            wrist = CGPoint(x: Double(root.x) + dx / d * reach, y: Double(root.y) + dy / d * reach)
            // The hand stays the same size, and his wrist gently lines it up with the arm.
            let want = CGPoint(x: Double(wrist.x) + dx / d * Self.hand, y: Double(wrist.y) + dy / d * Self.hand)
            tip = CGPoint(x: tip.x + (want.x - tip.x) * 0.08, y: tip.y + (want.y - tip.y) * 0.08)
            let hx = Double(tip.x - wrist.x), hy = Double(tip.y - wrist.y)
            let h = max(1e-6, hypot(hx, hy))
            tip = CGPoint(x: Double(wrist.x) + hx / h * Self.hand, y: Double(wrist.y) + hy / h * Self.hand)
        }
    }

    func pose(origin: CGPoint) -> ArmPose {
        ArmPose(wrist: CGPoint(x: wrist.x - origin.x, y: wrist.y - origin.y),
                angle: atan2(Double(tip.y - wrist.y), Double(tip.x - wrist.x)))
    }
}

// One Onkey's arms: walking hand over hand, dangling when picked up, and easing between.
final class ArmRig {
    // Walking: each hand is planted for this much of a stride, then lifts and reaches forward.
    static let stance = 0.55, travel = 150.0, lift = 55.0
    // How far his head bobs at most (canvas points), twice a stride.
    static let bob = 2.2

    private var phase = 0.0, direction = -1.0, across = 1.0
    private var walkMix = 0.0, walkingNow = false
    private var dangles: [Dangle]?
    private var dangleMix = 0.0, dangling = false
    private var boilClock = 0.0
    private(set) var seed = 0.0
    private(set) var poses = ArmSpec.both.map(ArmPose.rest)
    private(set) var bounce = 0.0           // Canvas points his head is lifted by.
    private(set) var moving = false         // Whether the arms are animating at all.

    // He walked this far this tick (sprite pixels; dx across the screen, + is right).
    func walked(dx: Double, distance: Double) {
        guard distance > 0 else { return }
        walkingNow = true
        if abs(dx) > distance * 0.2 { direction = dx > 0 ? 1 : -1 }
        // A planted hand slides back exactly as far as he moves, so it never skates.
        phase = (phase + distance * Self.stance / Self.travel).truncatingRemainder(dividingBy: 1)
        across = min(1, abs(dx) / distance)
    }

    // origin: where the canvas's top-left is, in world sprite pixels (y-down).
    // Returns whether anything about the arms changed.
    func update(dt: Double, origin: CGPoint, carried: Bool, floppy: Bool, sketchy: Bool) -> Bool {
        let before = (poses, bounce, seed)
        walkMix += ((walkingNow ? 1 : 0) - walkMix) * min(1, dt * 8)
        if !walkingNow && walkMix < 0.002 { walkMix = 0 }
        walkingNow = false

        let rest = ArmSpec.both.map(ArmPose.rest)
        var target = rest
        if walkMix > 0 {
            for (i, spec) in ArmSpec.both.enumerated() { target[i] = rest[i].mix(gait(spec), walkMix) }
        }
        bounce = Self.bob * abs(sin(2 * .pi * phase)) * walkMix

        let hang = carried && floppy
        if hang && !dangling {
            dangles = ArmSpec.both.enumerated().map { Dangle(spec: $1, origin: origin, pose: poses[$0]) }
            dangling = true
        } else if !hang && dangling {
            dangling = false
        }
        if dangling { dangleMix = 1 } else if dangleMix > 0 { dangleMix = max(0, dangleMix - dt / 0.45) }
        if dangleMix == 0 { dangles = nil }
        if var d = dangles {
            for (i, spec) in ArmSpec.both.enumerated() {
                d[i].step(spec: spec, origin: origin, dt: dt)
            }
            dangles = d
            // Settling back down overshoots a touch, so his hands slap onto the ground.
            let t = 1 - dangleMix, k = 1.7
            let eased = dangling ? 0 : 1 + (k + 1) * pow(t - 1, 3) + k * pow(t - 1, 2)
            for i in target.indices { target[i] = d[i].pose(origin: origin).mix(target[i], eased) }
        }
        poses = target
        moving = walkMix > 0 || dangleMix > 0
        if moving && sketchy {
            // Redrawn eight times a second, like animation on paper.
            boilClock += dt
            if boilClock >= 0.125 { boilClock = 0; seed = (seed + 2.39).truncatingRemainder(dividingBy: 100) }
        }
        return before.0 != poses || before.1 != bounce || before.2 != seed
    }

    func shoulder(_ spec: ArmSpec) -> CGPoint {
        CGPoint(x: spec.root.x, y: spec.root.y - CGFloat(bounce / OnkeyRenderer.spriteScale))
    }

    func shapes(sketchy: Bool) -> [ArmShape] {
        ArmSpec.both.enumerated().map { i, spec in
            ArmGeometry.shape(spec, root: shoulder(spec), pose: poses[i], seed: seed, boil: sketchy && moving ? 1.2 : 0)
        }
    }

    // Where a hand is in its stride, walking in `direction`.
    private func gait(_ spec: ArmSpec) -> ArmPose {
        let p = (phase + (spec.out < 0 ? 0 : 0.5)).truncatingRemainder(dividingBy: 1)
        let travel = Self.travel * across
        // The hand on the side he's heading reaches out further; the other one tucks in.
        let front = travel * (spec.out == direction ? 2.0 / 3 : 1.0 / 3)
        let fx = Double(spec.wrist.x) + direction * front, bx = Double(spec.wrist.x) - direction * (travel - front)
        if p < Self.stance {
            return ArmPose(wrist: CGPoint(x: fx + (bx - fx) * p / Self.stance, y: Double(spec.wrist.y)), angle: spec.restAngle)
        }
        let u = (p - Self.stance) / (1 - Self.stance)
        let ease = u * u * (3 - 2 * u)
        // The hand peels up off the ground and reaches forward, fingers first.
        let tilt = 0.56 * pow(sin(Double.pi * u), 0.8)
        return ArmPose(wrist: CGPoint(x: bx + (fx - bx) * ease, y: Double(spec.wrist.y) - Self.lift * sin(Double.pi * u)),
                       angle: spec.restAngle + (spec.out < 0 ? tilt : -tilt))
    }
}
