// Onkey desktop pet for macOS. The Windows version in ../Windows mirrors it: a
// transparent window that wanders the screen, rests between trips, and plays his
// sound now and then. Everything is adjustable from the menu bar icon.
import AppKit
import AVFoundation

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
    // Bop test: at rest and fully squashed (15%), pivoting on his base.
    for (name, squash) in [("bop-rest", 0.0), ("bop-squashed", 0.15)] {
        let still = r.render(phase: 0, walking: false, pixelsPerPoint: 4)!
        let view = PetView(frame: NSRect(x: 0, y: 0, width: OnkeyRenderer.canvas.width * 4, height: OnkeyRenderer.canvas.height * 4))
        view.show(still.image, scale: 1)
        view.setBase((OnkeyRenderer.canvas.height - still.opaqueBounds.maxY) * 4)
        view.setSquash(CGFloat(squash))
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

// `Onkey --beat-test <file>` runs a sound file through the beat tracker and prints what it
// hears every two seconds: whether there's a beat, its tempo, and when the last one landed.
if let i = CommandLine.arguments.firstIndex(of: "--beat-test"), i + 1 < CommandLine.arguments.count {
    let url = URL(fileURLWithPath: CommandLine.arguments[i + 1])
    guard let file = try? AVAudioFile(forReading: url),
          let buffer = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: AVAudioFrameCount(file.length)),
          (try? file.read(into: buffer)) != nil, let samples = buffer.floatChannelData?[0] else {
        print("Could not read \(url.path)"); exit(1)
    }
    let rate = file.processingFormat.sampleRate, total = Int(buffer.frameLength)
    let tracker = BeatTracker(sampleRate: rate)
    var done = 0, nextReport = 2.0
    while done < total {
        let n = min(512, total - done)
        tracker.process(UnsafeBufferPointer(start: samples + done, count: n), endingAt: Double(done + n - 1) / rate)
        done += n
        let t = Double(done) / rate
        if t >= nextReport {
            let b = tracker.current
            print(String(format: "t=%5.1f active=%@ bpm=%6.1f lastBeat=%.3f confidence=%.2f", t, b.active ? "yes" : "no ",
                         60 / b.period, b.lastBeat, b.confidence))
            nextReport += 0.5
        }
    }
    exit(0)
}

Log.start()
let app = NSApplication.shared
let delegate = OnkeyApp()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
