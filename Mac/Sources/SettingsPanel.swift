import AppKit
import ServiceManagement
import SwiftUI

// The settings drop-down: a hand-drawn jungle panel that hangs from the menu bar icon, with
// quick switches along the top and a tab for each group of settings. Everything is drawn
// here (sketchy lines, vine sliders, leaf switches) rather than taken from stock controls,
// and every change takes effect straight away. It closes when you click anywhere else.
// The Windows version in Windows/Source/SettingsPanel.cs looks the same.
final class SettingsPanel {
    static let size = CGSize(width: 380, height: 616)
    private let panel: Panel
    private let model: SettingsModel
    private var clickMonitor: Any?
    var isShown: Bool { panel.isVisible && !closing }
    private var closing = false
    private var showCount = 0   // Lets a close that finishes after a reopen leave the panel alone.
    var onClose: (() -> Void)?

    // A borderless panel that can still take key presses (for Escape).
    private final class Panel: NSPanel {
        var onCancel: (() -> Void)?
        override var canBecomeKey: Bool { true }
        override func cancelOperation(_ sender: Any?) { onCancel?() }
    }

    init(app: OnkeyApp) {
        model = SettingsModel(app: app)
        panel = Panel(contentRect: NSRect(origin: .zero, size: Self.size),
                      styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .popUpMenu
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        panel.contentView = NSHostingView(rootView: SettingsView(model: model))
        panel.onCancel = { [weak self] in self?.close() }
        app.onSettingsChanged = { [weak model] in model?.objectWillChange.send() }
    }

    // Shows the panel hanging under the menu bar icon, kept on its screen.
    func show(below button: NSStatusBarButton) {
        guard let buttonWindow = button.window else { return }
        let anchor = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        let screen = (buttonWindow.screen ?? NSScreen.main ?? NSScreen.screens[0]).visibleFrame
        let x = max(screen.minX + 8, min(anchor.midX - Self.size.width / 2, screen.maxX - Self.size.width - 8))
        let home = NSPoint(x: x, y: anchor.minY - Self.size.height - 6)
        model.objectWillChange.send()
        if model.tab == SettingsView.updatesTab { model.app.updater.checkIfStale() }
        // It fades in while dropping the last few points into place under the icon.
        closing = false
        showCount += 1
        if !panel.isVisible {
            panel.alphaValue = 0
            panel.setFrameOrigin(NSPoint(x: home.x, y: home.y + 10))
        }
        panel.makeKeyAndOrderFront(nil)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.18
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            panel.animator().alphaValue = 1
            panel.animator().setFrame(NSRect(origin: home, size: Self.size), display: true)
        }
        if let clickMonitor { NSEvent.removeMonitor(clickMonitor) }
        // Clicking anywhere outside Onkey closes it, like a menu.
        clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            guard let self, !self.panel.frame.contains(NSEvent.mouseLocation) else { return }
            self.close()
        }
    }

    func showTab(_ tab: Int) {
        withAnimation(.easeInOut(duration: 0.16)) { model.tab = tab }
        if tab == SettingsView.updatesTab { model.app.updater.checkIfStale() }
    }

    // Fades out while lifting back toward the icon, then hides.
    func close() {
        guard panel.isVisible, !closing else { return }
        closing = true
        if let clickMonitor { NSEvent.removeMonitor(clickMonitor) }
        clickMonitor = nil
        onClose?()
        let shown = showCount
        let origin = panel.frame.origin
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.13
            context.timingFunction = CAMediaTimingFunction(name: .easeIn)
            panel.animator().alphaValue = 0
            panel.animator().setFrame(NSRect(origin: NSPoint(x: origin.x, y: origin.y + 6), size: Self.size), display: true)
        }, completionHandler: { [weak self] in
            guard let self, self.closing, self.showCount == shown else { return }
            self.panel.orderOut(nil)
            self.closing = false
        })
    }
}

final class SettingsModel: ObservableObject {
    unowned let app: OnkeyApp
    let head: NSImage
    @Published var tab = 0

    init(app: OnkeyApp) {
        self.app = app
        head = app.headImage(height: 40)
    }

    func bool(_ key: String) -> Bool { app.defaults.bool(forKey: key) }
    func number(_ key: String) -> Double { app.defaults.double(forKey: key) }
    func string(_ key: String) -> String { app.defaults.string(forKey: key) ?? "" }
    func set(_ key: String, _ value: Any) { app.change(key, to: value) }

    func binding(_ key: String) -> Binding<Bool> {
        Binding(get: { self.bool(key) }, set: { self.set(key, $0) })
    }
}

// MARK: Look

enum Jungle {
    static let night = Color(red: 28 / 255, green: 52 / 255, blue: 34 / 255)
    static let canopy = Color(red: 40 / 255, green: 74 / 255, blue: 44 / 255)
    static let paper = Color(red: 246 / 255, green: 237 / 255, blue: 211 / 255)
    static let paperPressed = Color(red: 226 / 255, green: 214 / 255, blue: 180 / 255)
    static let pod = Color(red: 222 / 255, green: 208 / 255, blue: 172 / 255)
    static let ink = Color(red: 58 / 255, green: 40 / 255, blue: 24 / 255)
    static let faded = Color(red: 140 / 255, green: 120 / 255, blue: 94 / 255)
    static let bark = Color(red: 146 / 255, green: 98 / 255, blue: 54 / 255)
    static let leaf = Color(red: 92 / 255, green: 146 / 255, blue: 62 / 255)
    static let leafDark = Color(red: 56 / 255, green: 104 / 255, blue: 46 / 255)
    static let leafMid = Color(red: 72 / 255, green: 128 / 255, blue: 56 / 255)
    static let leafBack = Color(red: 34 / 255, green: 70 / 255, blue: 38 / 255)
    static let banana = Color(red: 244 / 255, green: 200 / 255, blue: 66 / 255)
    static let berry = Color(red: 214 / 255, green: 84 / 255, blue: 52 / 255)
    static let vine = Color(red: 104 / 255, green: 122 / 255, blue: 52 / 255)
    static let dryVine = Color(red: 196 / 255, green: 176 / 255, blue: 130 / 255)

    // macOS's own handwriting fonts, best first.
    static func hand(_ size: CGFloat, bold: Bool = false) -> Font {
        let names = bold ? ["ChalkboardSE-Bold", "MarkerFelt-Wide"] : ["ChalkboardSE-Regular", "MarkerFelt-Thin"]
        for name in names where NSFont(name: name, size: size) != nil { return .custom(name, size: size) }
        return .system(size: size, weight: bold ? .bold : .regular, design: .rounded)
    }

    static let title = hand(22, bold: true)
    static let tab = hand(13.5, bold: true)
    static let label = hand(14.5, bold: true)
    static let chip = hand(12.5, bold: true)
    static let small = hand(11.5)
    static let number = hand(22, bold: true)
}

// A random number generator that gives the same numbers for the same seed, so each
// sketchy line keeps its wobble from one redraw to the next.
struct Seeded: RandomNumberGenerator {
    private var state: UInt64
    init(_ seed: Int) { state = UInt64(truncatingIfNeeded: seed) &* 0x9E3779B97F4A7C15 &+ 1 }
    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
    mutating func wobble(_ amount: CGFloat) -> CGFloat { CGFloat.random(in: -amount...amount, using: &self) }
    mutating func unit() -> CGFloat { CGFloat.random(in: 0...1, using: &self) }
}

enum Sketch {
    static func box(_ r: CGRect, radius: CGFloat, seed: Int, wobble: CGFloat = 1.3) -> Path {
        let radius = min(radius, min(r.width, r.height) / 2)
        var outline: [CGPoint] = []
        corner(&outline, CGPoint(x: r.maxX - radius, y: r.minY + radius), radius, -90)
        corner(&outline, CGPoint(x: r.maxX - radius, y: r.maxY - radius), radius, 0)
        corner(&outline, CGPoint(x: r.minX + radius, y: r.maxY - radius), radius, 90)
        corner(&outline, CGPoint(x: r.minX + radius, y: r.minY + radius), radius, 180)
        return shaken(outline, seed: seed, wobble: wobble, closed: true)
    }

    // A tab: rounded on top, open at the bottom.
    static func tab(_ r: CGRect, seed: Int) -> Path {
        var outline = [CGPoint(x: r.minX, y: r.maxY)]
        corner(&outline, CGPoint(x: r.minX + 12, y: r.minY + 12), 12, 180)
        corner(&outline, CGPoint(x: r.maxX - 12, y: r.minY + 12), 12, 270)
        outline.append(CGPoint(x: r.maxX, y: r.maxY))
        return shaken(outline, seed: seed, wobble: 1.2, closed: false)
    }

    static func circle(_ c: CGPoint, _ radius: CGFloat, seed: Int, wobble: CGFloat = 0.9) -> Path {
        let n = max(10, Int(radius * 1.2))
        let outline = (0..<n).map { i -> CGPoint in
            let a = Double(i) * 2 * .pi / Double(n)
            return CGPoint(x: c.x + CGFloat(cos(a)) * radius, y: c.y + CGFloat(sin(a)) * radius)
        }
        return shaken(outline, seed: seed, wobble: wobble, closed: true)
    }

    // A hand-drawn line from a to b.
    static func line(_ a: CGPoint, _ b: CGPoint, seed: Int, wobble: CGFloat) -> Path {
        var rng = Seeded(seed)
        let dx = b.x - a.x, dy = b.y - a.y
        let length = max(1, (dx * dx + dy * dy).squareRoot())
        let n = max(2, Int(length / 16))
        let nx = -dy / length, ny = dx / length
        let points = (0...n).map { i -> CGPoint in
            let t = CGFloat(i) / CGFloat(n)
            let off = i == 0 || i == n ? 0 : rng.wobble(wobble)
            return CGPoint(x: a.x + dx * t + nx * off, y: a.y + dy * t + ny * off)
        }
        return smooth(points, closed: false)
    }

    // A leaf from `base`, pointing along `angle` (degrees, clockwise from +x).
    static func leaf(at base: CGPoint, angle: CGFloat, length: CGFloat, width: CGFloat) -> (shape: Path, veins: Path) {
        var shape = Path()
        shape.move(to: .zero)
        shape.addCurve(to: CGPoint(x: length, y: 0), control1: CGPoint(x: length * 0.3, y: -width),
                       control2: CGPoint(x: length * 0.75, y: -width * 0.8))
        shape.addCurve(to: .zero, control1: CGPoint(x: length * 0.75, y: width * 0.8),
                       control2: CGPoint(x: length * 0.3, y: width))
        var veins = Path()
        veins.move(to: .zero)
        veins.addLine(to: CGPoint(x: length * 0.9, y: 0))
        for i in 1...3 {
            let x = length * CGFloat(i) / 4.5
            veins.move(to: CGPoint(x: x, y: 0)); veins.addLine(to: CGPoint(x: x + length * 0.12, y: -width * 0.45))
            veins.move(to: CGPoint(x: x, y: 0)); veins.addLine(to: CGPoint(x: x + length * 0.12, y: width * 0.45))
        }
        let t = CGAffineTransform(translationX: base.x, y: base.y).rotated(by: angle * .pi / 180)
        return (shape.applying(t), veins.applying(t))
    }

    private static func corner(_ outline: inout [CGPoint], _ c: CGPoint, _ radius: CGFloat, _ start: Double) {
        for i in 0...3 {
            let a = (start + Double(i) * 30) * .pi / 180
            outline.append(CGPoint(x: c.x + CGFloat(cos(a)) * radius, y: c.y + CGFloat(sin(a)) * radius))
        }
    }

    private static func shaken(_ outline: [CGPoint], seed: Int, wobble: CGFloat, closed: Bool) -> Path {
        // Fill in long straight runs so they wobble too.
        var points: [CGPoint] = []
        let count = closed ? outline.count : outline.count - 1
        for i in 0..<count {
            let a = outline[i], b = outline[(i + 1) % outline.count]
            let length = ((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)).squareRoot()
            let steps = max(1, Int(length / 22))
            for s in 0..<steps {
                let t = CGFloat(s) / CGFloat(steps)
                points.append(CGPoint(x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t))
            }
        }
        if !closed { points.append(outline[outline.count - 1]) }
        var rng = Seeded(seed)
        for i in points.indices where closed || (i > 0 && i < points.count - 1) {
            points[i].x += rng.wobble(wobble)
            points[i].y += rng.wobble(wobble)
        }
        return smooth(points, closed: closed)
    }

    // A Catmull-Rom curve through the points.
    private static func smooth(_ p: [CGPoint], closed: Bool) -> Path {
        var path = Path()
        guard p.count > 1 else { return path }
        let n = p.count
        func at(_ i: Int) -> CGPoint { closed ? p[(i % n + n) % n] : p[min(max(i, 0), n - 1)] }
        path.move(to: p[0])
        let segments = closed ? n : n - 1
        let k: CGFloat = 0.4 / 3 * 2   // Tension, matching GDI+'s curves at 0.4.
        for i in 0..<segments {
            let p0 = at(i - 1), p1 = at(i), p2 = at(i + 1), p3 = at(i + 2)
            let c1 = CGPoint(x: p1.x + (p2.x - p0.x) * k / 2, y: p1.y + (p2.y - p0.y) * k / 2)
            let c2 = CGPoint(x: p2.x - (p3.x - p1.x) * k / 2, y: p2.y - (p3.y - p1.y) * k / 2)
            path.addCurve(to: p2, control1: c1, control2: c2)
        }
        if closed { path.closeSubpath() }
        return path
    }

    static func seed(_ r: CGRect) -> Int { Int(r.minX * 31 + r.minY * 17 + r.width * 7) }
}

extension GraphicsContext {
    // Two passes of the pencil, the second lighter and slightly off.
    func pencil(_ path: Path, _ color: Color, _ width: CGFloat) {
        stroke(path, with: .color(color), style: StrokeStyle(lineWidth: width, lineCap: .round, lineJoin: .round))
        stroke(path.applying(CGAffineTransform(translationX: 0.8, y: -0.6)), with: .color(color.opacity(0.33)),
               style: StrokeStyle(lineWidth: width * 0.6, lineCap: .round, lineJoin: .round))
    }

    func leaf(at base: CGPoint, angle: CGFloat, length: CGFloat, width: CGFloat, color: Color) {
        let l = Sketch.leaf(at: base, angle: angle, length: length, width: width)
        fill(l.shape, with: .color(color))
        pencil(l.shape, Jungle.ink.opacity(0.6), 1.4)
        stroke(l.veins, with: .color(Jungle.ink.opacity(0.43)), lineWidth: 1.2)
    }
}

// A shape drawn in a Canvas that fills its frame: fill, then a pencil outline.
struct SketchBox: View {
    var radius: CGFloat = 12
    let seed: Int
    let fill: Color
    var line: CGFloat = 1.8
    var wobble: CGFloat = 1.3

    var body: some View {
        Canvas { ctx, size in
            let path = Sketch.box(CGRect(origin: .zero, size: size).insetBy(dx: 1.5, dy: 1.5), radius: radius, seed: seed, wobble: wobble)
            ctx.fill(path, with: .color(fill))
            ctx.pencil(path, Jungle.ink, line)
        }
    }
}

// MARK: Panel

struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    private var tab: Int { model.tab }
    private let tabs = ["Moves", "Sound", "Look", "Updates"]
    static let updatesTab = 3
    private static let cardTop: CGFloat = 124, footerHeight: CGFloat = 50

    var body: some View {
        ZStack(alignment: .topLeading) {
            JungleBackdrop()
            HStack(spacing: 8) {
                Image(nsImage: model.head).resizable().interpolation(.high).aspectRatio(contentMode: .fit)
                    .frame(width: 40, height: 40)
                Text("Onkey").font(Jungle.title).foregroundColor(Jungle.paper)
            }
            .padding(.leading, 14).padding(.top, 8)
            HStack(spacing: 8) {
                QuickToggle(title: "Pause", isOn: model.app.isPaused) { model.app.togglePause() }
                QuickToggle(title: "Dance", isOn: model.bool(Key.dance)) { model.set(Key.dance, !model.bool(Key.dance)) }
                QuickToggle(title: "Drag him", isOn: model.bool(Key.draggable)) { model.set(Key.draggable, !model.bool(Key.draggable)) }
            }
            .offset(x: 18, y: 56)
            ForEach(tabs.indices, id: \.self) { i in
                if i != tab { tabButton(i) }
            }
            card
            tabButton(tab)
            page.padding(.top, Self.cardTop + 18).padding(.horizontal, 30)
            footer
        }
        .frame(width: SettingsPanel.size.width, height: SettingsPanel.size.height, alignment: .topLeading)
        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).stroke(Jungle.ink, lineWidth: 2))
    }

    private var card: some View {
        Canvas { ctx, size in
            let r = CGRect(x: 12, y: Self.cardTop, width: size.width - 24, height: size.height - Self.cardTop - 12)
            ctx.fill(Sketch.box(r.offsetBy(dx: 4, dy: 6), radius: 14, seed: 11, wobble: 1.5), with: .color(.black.opacity(0.35)))
            let path = Sketch.box(r, radius: 14, seed: 11, wobble: 1.5)
            ctx.fill(path, with: .color(Jungle.paper))
            ctx.pencil(path, Jungle.ink, 2.2)
            let ruleY = size.height - 12 - Self.footerHeight
            ctx.stroke(Sketch.line(CGPoint(x: 30, y: ruleY), CGPoint(x: size.width - 30, y: ruleY), seed: 4, wobble: 1.2),
                       with: .color(Jungle.ink.opacity(0.35)), lineWidth: 1.2)
        }
        .allowsHitTesting(false)
    }

    private var footer: some View {
        HStack(spacing: 8) {
            SignButton(title: "Quit Onkey", small: true) { model.app.quit() }
            // After a crash it says so, with a berry on it.
            let crashed = CrashReport.pending
            SignButton(title: crashed ? "Send crash report" : "Report a problem", small: true) { model.app.reportProblem() }
                .overlay(alignment: .topTrailing) {
                    if crashed {
                        Circle().fill(Jungle.berry).overlay(Circle().stroke(Jungle.ink, lineWidth: 1.2))
                            .frame(width: 10, height: 10).offset(x: 2, y: -3).allowsHitTesting(false)
                    }
                }
            Spacer()
            Text("v\(Updater.current)").font(Jungle.small).foregroundColor(Jungle.faded)
        }
        .padding(.horizontal, 30)
        .frame(width: SettingsPanel.size.width, height: Self.footerHeight)
        .offset(y: SettingsPanel.size.height - 12 - Self.footerHeight)
    }

    private func tabButton(_ i: Int) -> some View {
        let selected = i == tab
        let width: CGFloat = 78, height: CGFloat = selected ? 36 : 31
        let x = 12 + 10 + CGFloat(i) * (width + 6)
        return ZStack {
            Canvas { ctx, size in
                let path = Sketch.tab(CGRect(origin: .zero, size: size).insetBy(dx: 1.5, dy: 0), seed: Int(x) * 7 + (selected ? 1 : 0))
                ctx.fill(path, with: .color(selected ? Jungle.paper : Jungle.bark))
                if !selected {
                    // Wood grain.
                    for g in 1..<3 {
                        let y = CGFloat(g) * size.height / 3
                        ctx.stroke(Sketch.line(CGPoint(x: 8, y: y), CGPoint(x: size.width - 8, y: y + 2), seed: Int(x) + g, wobble: 1.2),
                                   with: .color(Jungle.ink.opacity(0.27)), lineWidth: 1)
                    }
                }
                ctx.pencil(path, Jungle.ink, 2)
                if selected {
                    // Hide the card's top edge under the open tab.
                    ctx.fill(Path(CGRect(x: 3, y: size.height - 4, width: size.width - 6, height: 8)), with: .color(Jungle.paper))
                }
            }
            Text(tabs[i]).font(Jungle.tab).foregroundColor(selected ? Jungle.ink : Jungle.paper)
                .offset(y: selected ? -2 : 0)
            if i == Self.updatesTab && model.app.updater.available != nil {
                // A banana on the Updates tab while there's a new Onkey to get.
                Canvas { ctx, size in
                    let dot = Sketch.circle(CGPoint(x: size.width / 2, y: size.height / 2), 5, seed: 5, wobble: 0.5)
                    ctx.fill(dot, with: .color(Jungle.banana))
                    ctx.stroke(dot, with: .color(Jungle.ink), lineWidth: 1.2)
                }
                .frame(width: 11, height: 11)
                .offset(x: width / 2 - 5, y: -height / 2 + 3)
            }
        }
        .frame(width: width, height: height + (selected ? 4 : 0))
        .contentShape(Rectangle())
        .onTapGesture { select(i) }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(tabs[i])
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : .isButton)
        .accessibilityValue([selected ? "selected" : "", i == Self.updatesTab && model.app.updater.available != nil ? "new version" : ""]
            .filter { !$0.isEmpty }.joined(separator: ", "))
        .accessibilityAction { select(i) }
        .offset(x: x, y: Self.cardTop - height + (selected ? 4 : 0))
    }

    private func select(_ i: Int) {
        withAnimation(.easeInOut(duration: 0.16)) { model.tab = i }
        if i == Self.updatesTab { model.app.updater.checkIfStale() }
    }

    @ViewBuilder private var page: some View {
        switch tab {
        case 0: MovesPage(model: model)
        case 1: SoundPage(model: model)
        case 2: LookPage(model: model)
        default: UpdatesPage(model: model)
        }
    }
}

struct JungleBackdrop: View {
    var body: some View {
        Canvas { ctx, size in
            let w = size.width, h = size.height
            ctx.fill(Path(CGRect(origin: .zero, size: size)),
                     with: .linearGradient(Gradient(colors: [Jungle.canopy, Jungle.night]), startPoint: .zero, endPoint: CGPoint(x: 0, y: h)))
            var r = Seeded(7)
            // Dappled light through the canopy.
            for _ in 0..<26 {
                let s = 20 + r.unit() * 60
                ctx.fill(Path(ellipseIn: CGRect(x: r.unit() * w, y: r.unit() * h, width: s, height: s * 0.7)),
                         with: .color(Color(red: 200 / 255, green: 230 / 255, blue: 140 / 255).opacity(0.06 + Double(r.unit()) * 0.05)))
            }
            // Big leaves crowding in from the edges, darkest at the back.
            let greens = [Jungle.leafBack, Jungle.leafDark, Jungle.leafMid, Jungle.leaf]
            for (layer, green) in greens.enumerated() {
                for _ in 0..<9 {
                    let side = Int(r.unit() * 3.999), along = r.unit()
                    let at = side == 0 ? CGPoint(x: along * w, y: -6) : side == 1 ? CGPoint(x: w + 6, y: along * h)
                           : side == 2 ? CGPoint(x: along * w, y: h + 6) : CGPoint(x: -6, y: along * h)
                    let toward = atan2(h / 2 - at.y, w / 2 - at.x) * 180 / .pi + (r.unit() * 70 - 35)
                    let length = 70 + r.unit() * 70 - CGFloat(layer) * 8
                    ctx.leaf(at: at, angle: toward, length: length, width: length * 0.42, color: green)
                }
            }
            // Vines hanging from the top.
            for i in 0..<5 {
                let x = 40 + CGFloat(i) * (w - 80) / 4 + r.unit() * 30
                let len = 40 + r.unit() * 50
                let end = CGPoint(x: x + r.unit() * 16 - 8, y: len)
                ctx.stroke(Sketch.line(CGPoint(x: x, y: -4), end, seed: i * 13, wobble: 3), with: .color(Jungle.vine), lineWidth: 3)
                ctx.leaf(at: end, angle: 70 + r.unit() * 40, length: 22, width: 11, color: Jungle.leaf)
                ctx.leaf(at: CGPoint(x: x + 1, y: len * 0.5), angle: 200 + r.unit() * 30, length: 18, width: 9, color: Jungle.leaf)
            }
        }
        // Catches clicks on empty parts of the panel, so they don't fall through to the window behind.
        .contentShape(Rectangle())
        .onTapGesture {}
    }
}

// MARK: Pages

// A setting with its name (and a note about it) on the left and a small control on the right.
struct InlineRow<Control: View>: View {
    let label: String
    var caption: String? = nil
    var enabled = true
    @ViewBuilder let control: Control

    var body: some View {
        HStack(alignment: .center, spacing: 8) {
            VStack(alignment: .leading, spacing: 0) {
                Text(label).font(Jungle.label).foregroundColor(enabled ? Jungle.ink : Jungle.faded)
                if let caption { Text(caption).font(Jungle.small).foregroundColor(Jungle.faded) }
            }
            Spacer(minLength: 0)
            control
        }
        .frame(minHeight: 40)
    }
}

// A setting with its name and note on one line and its control across the card below.
struct StackedRow<Control: View>: View {
    let label: String
    var caption: String? = nil
    var enabled = true
    @ViewBuilder let control: Control

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .firstTextBaseline) {
                Text(label).font(Jungle.label).foregroundColor(enabled ? Jungle.ink : Jungle.faded)
                Spacer(minLength: 0)
                if let caption { Text(caption).font(Jungle.small).foregroundColor(Jungle.faded) }
            }
            control
        }
    }
}

struct MovesPage: View {
    @ObservedObject var model: SettingsModel
    // "chase" is how many trips out of N head for the mouse (0 for never).
    private let chaseValues = [0, 5, 2, 1], chaseNames = ["never", "sometimes", "often", "always"]

    var body: some View {
        let count = model.app.defaults.integer(forKey: Key.count)
        let chase = max(0, chaseValues.firstIndex(of: model.app.defaults.integer(forKey: Key.chase)) ?? 0)
        let speed = model.number(Key.speed)
        VStack(alignment: .leading, spacing: 10) {
            InlineRow(label: "How many Onkeys", caption: count <= 1 ? "just the one" : count >= 8 ? "total chaos" : "a little troop") {
                CoconutStepper(value: count, range: 1...20) { model.set(Key.count, $0) }
            }
            StackedRow(label: "Where he roams") {
                Chips(options: [("Anywhere", "anywhere"), ("Bottom", "bottom"), ("Top", "top"),
                                ("Left side", "left"), ("Right side", "right"), ("Stay put", "stay")],
                      selected: model.string(Key.zone)) { model.set(Key.zone, $0) }
            }
            StackedRow(label: "Walks to my mouse", caption: chaseNames[chase]) {
                VineSlider("Walks to my mouse", value: Double(chase), range: 0...3, step: 1, spoken: chaseNames[chase]) { model.set(Key.chase, chaseValues[Int($0.rounded())]) }
            }
            StackedRow(label: "Walking speed", caption: speed < 30 ? "a slow stroll" : speed < 60 ? "normal" : speed < 120 ? "fast" : "zoomies!") {
                VineSlider("Walking speed", value: speed, range: 10...200, spoken: "\(Int(speed))") { model.set(Key.speed, $0.rounded()) }
            }
            InlineRow(label: "Floppy arms", caption: "they dangle when you carry him") { LeafSwitch(isOn: model.binding(Key.floppyArms), label: "Floppy arms") }
            SignButton(title: "Bring Onkey to this screen") { model.app.bringHere() }
        }
    }
}

struct SoundPage: View {
    @ObservedObject var model: SettingsModel
    // Seconds between his sounds, at least: he waits between this and twice this.
    private static let gaps: [Double] = [10, 20, 30, 45, 60, 90, 120, 180, 300, 600]

    var body: some View {
        let on = model.app.hasSound && model.bool(Key.soundOn)
        let gap = model.number(Key.soundGap)
        let gapIndex = Self.gaps.indices.min { abs(Self.gaps[$0] - gap) < abs(Self.gaps[$1] - gap) } ?? 5
        VStack(alignment: .leading, spacing: 10) {
            InlineRow(label: "Sound", caption: model.app.hasSound ? "his oooo now and then" : "sound clip not installed") {
                LeafSwitch(isOn: model.binding(Key.soundOn), label: "Sound on").disabled(!model.app.hasSound)
            }
            StackedRow(label: "How often", caption: Self.every(gap), enabled: on) {
                VineSlider("How often", value: Double(gapIndex), range: 0...Double(Self.gaps.count - 1), step: 1, spoken: Self.every(gap)) {
                    model.set(Key.soundGap, Self.gaps[Int($0.rounded())])
                }.disabled(!on)
            }
            StackedRow(label: "Volume", caption: percent(model.number(Key.volume)), enabled: on) {
                VineSlider("Volume", value: model.number(Key.volume), range: 0.05...1, spoken: percent(model.number(Key.volume))) { model.set(Key.volume, ($0 * 100).rounded() / 100) }
                    .disabled(!on)
            }
            SignButton(title: "Say oooo now") { model.app.playNow() }.disabled(!model.app.hasSound)
        }
    }

    private static func every(_ gap: Double) -> String {
        if gap * 2 < 120 { return "every \(Int(gap))-\(Int(gap * 2)) sec" }
        return "every \(minutes(gap))-\(minutes(gap * 2)) min"
    }

    private static func minutes(_ seconds: Double) -> String {
        let m = seconds / 60
        if abs(m - m.rounded(.down) - 0.5) < 0.01 { return m < 1 ? "½" : "\(Int(m))½" }
        return "\(Int(m.rounded()))"
    }
}

struct LookPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        let opacity = model.number(Key.opacity)
        let desktop = model.string(Key.layer) == "desktop"
        VStack(alignment: .leading, spacing: 8) {
            StackedRow(label: "Size", caption: percent(model.number(Key.size))) {
                // Re-rendering every frame is too slow to follow the mouse, so size waits for the drop.
                VineSlider("Size", value: model.number(Key.size), range: 0.4...3, onDrop: true, spoken: percent(model.number(Key.size))) { model.set(Key.size, ($0 * 100).rounded() / 100) }
            }
            StackedRow(label: "See-through", caption: opacity > 0.95 ? "solid" : opacity < 0.45 ? "ghostly" : "\(percent(opacity)) solid") {
                VineSlider("See-through", value: opacity, range: 0.2...1, spoken: percent(opacity)) { model.set(Key.opacity, ($0 * 100).rounded() / 100) }
            }
            StackedRow(label: "Where he lives") {
                Chips(options: [("In front", "above"), ("On the desktop", "desktop")], selected: model.string(Key.layer)) {
                    model.set(Key.layer, $0)
                }
            }
            InlineRow(label: "Over full-screen apps", caption: "videos, games, slideshows", enabled: !desktop) {
                LeafSwitch(isOn: model.binding(Key.overFullScreen), label: "Over full-screen apps").disabled(desktop)
            }
            InlineRow(label: "Watch my cursor", caption: "his eyes follow the mouse") { LeafSwitch(isOn: model.binding(Key.watchCursor), label: "Watch my cursor") }
            InlineRow(label: "Blink", caption: "now and then") { LeafSwitch(isOn: model.binding(Key.blink), label: "Blink") }
            InlineRow(label: "Sketchy arms", caption: "lines wiggle like a drawing") { LeafSwitch(isOn: model.binding(Key.sketchy), label: "Sketchy arms") }
        }
    }
}

struct UpdatesPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            let updater = model.app.updater
            UpdateCard(updater: updater, phase: updater.phase, progress: updater.progress, head: model.head).frame(height: 236)
            InlineRow(label: "Check by himself", caption: "looks every few hours") { LeafSwitch(isOn: model.binding(Key.checkUpdates), label: "Check by himself") }
            InlineRow(label: "Open at login", caption: "when you log in to your Mac") {
                LeafSwitch(isOn: Binding(get: { model.app.opensAtLogin }, set: { _ in model.app.toggleLogin() }), label: "Open at login")
            }
        }
    }
}

// The whole update, in one sketched box: looking for a new Onkey, what's new in it, the
// download growing along a vine, and restarting. Each step fades into the next.
struct UpdateCard: View {
    let updater: Updater
    // Passed in (rather than read from the updater) so SwiftUI sees each change.
    let phase: Updater.Phase
    let progress: Double
    let head: NSImage

    private var step: String {
        switch phase {
        case .idle, .checking: return "checking"
        case .upToDate: return "current"
        case .available: return "available"
        case .downloading: return "downloading"
        case .restarting: return "restarting"
        case .failed: return "failed"
        }
    }

    var body: some View {
        ZStack {
            SketchBox(radius: 12, seed: 21, fill: Jungle.pod.opacity(0.5), line: 1.6, wobble: 1.2)
            content
                .padding(14)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .id(step)
                .transition(.opacity.combined(with: .scale(scale: 0.97)))
        }
        .animation(.easeInOut(duration: 0.25), value: step)
    }

    @ViewBuilder private var content: some View {
        let version = updater.available?.version ?? ""
        switch phase {
        case .idle, .checking:
            VStack(spacing: 10) {
                Spacer()
                BouncingCoconuts().frame(width: 120, height: 56)
                Text("Looking for a new Onkey…").font(Jungle.label).foregroundColor(Jungle.ink)
                Text("You have Onkey \(Updater.current).").font(Jungle.small).foregroundColor(Jungle.faded)
                Spacer()
            }
        case .upToDate:
            VStack(spacing: 8) {
                Spacer()
                Image(nsImage: head).resizable().interpolation(.high).aspectRatio(contentMode: .fit).frame(width: 52, height: 52)
                    .accessibilityHidden(true)
                Text("You're up to date!").font(Jungle.label).foregroundColor(Jungle.ink)
                Text("Onkey \(Updater.current) is the newest one.").font(Jungle.small).foregroundColor(Jungle.faded)
                SignButton(title: "Check again", small: true) { updater.check() }.padding(.top, 4)
                Spacer()
            }
        case .available:
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Onkey \(version) is out!").font(Jungle.label).foregroundColor(Jungle.ink)
                    Spacer()
                    Text("you have \(Updater.current)").font(Jungle.small).foregroundColor(Jungle.faded)
                }
                ScrollView {
                    Text(updater.available?.notes.isEmpty == false ? updater.available!.notes : "A new version of Onkey.")
                        .font(Jungle.small).foregroundColor(Jungle.ink)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxHeight: .infinity)
                HStack {
                    Spacer()
                    SignButton(title: "Update and restart") { updater.install() }
                    Spacer()
                }
                if Updater.installTarget() != Bundle.main.bundleURL {
                    Text("He'll move into your Applications folder.").font(Jungle.small).foregroundColor(Jungle.faded)
                        .frame(maxWidth: .infinity)
                }
            }
        case .downloading:
            VStack(spacing: 10) {
                Spacer()
                Text("Downloading Onkey \(version)…").font(Jungle.label).foregroundColor(Jungle.ink)
                VineProgress(progress: progress)
                    .animation(.easeOut(duration: 0.3), value: progress)
                    .frame(height: 40)
                    .accessibilityElement()
                    .accessibilityLabel("Download")
                    .accessibilityValue(percent(progress))
                Text(percent(progress)).font(Jungle.small).foregroundColor(Jungle.faded).accessibilityHidden(true)
                Spacer()
            }
        case .restarting:
            VStack(spacing: 10) {
                Spacer()
                BouncingCoconuts().frame(width: 120, height: 56)
                Text("Restarting with Onkey \(version)…").font(Jungle.label).foregroundColor(Jungle.ink)
                Text("Your settings come with him.").font(Jungle.small).foregroundColor(Jungle.faded)
                Spacer()
            }
        case .failed(let message):
            VStack(spacing: 8) {
                Spacer()
                Text("Hmm, that didn't work").font(Jungle.label).foregroundColor(Jungle.ink)
                Text(message).font(Jungle.small).foregroundColor(Jungle.faded).multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                SignButton(title: "Try again", small: true) { updater.check() }.padding(.top, 4)
                Spacer()
            }
        }
    }
}

// Three coconuts bouncing one after another: the loading animation.
struct BouncingCoconuts: View {
    var body: some View {
        TimelineView(.animation) { timeline in
            let t = timeline.date.timeIntervalSinceReferenceDate
            Canvas { ctx, size in
                let ground = size.height - 8
                ctx.stroke(Sketch.line(CGPoint(x: 6, y: ground + 4), CGPoint(x: size.width - 6, y: ground + 4), seed: 9, wobble: 1.5),
                           with: .color(Jungle.vine), style: StrokeStyle(lineWidth: 3, lineCap: .round))
                for i in 0..<3 {
                    let phase = (t * 2.2 - Double(i) * 0.22).truncatingRemainder(dividingBy: 1)
                    let p = phase < 0 ? phase + 1 : phase
                    let hop = CGFloat(sin(p * .pi)) * 26          // Up and back down each beat.
                    let squash = p < 0.08 || p > 0.92 ? 0.85 : 1.0 // A little squash on landing.
                    let x = size.width / 2 + CGFloat(i - 1) * 34
                    let r: CGFloat = 10
                    let center = CGPoint(x: x, y: ground - r * CGFloat(squash) - hop)
                    // Its shadow shrinks as it rises.
                    let shadow = r * (1 - hop / 60)
                    ctx.fill(Path(ellipseIn: CGRect(x: x - shadow, y: ground, width: shadow * 2, height: 4)), with: .color(Jungle.ink.opacity(0.18)))
                    var nut = ctx
                    nut.translateBy(x: center.x, y: center.y)
                    nut.scaleBy(x: 1 / CGFloat(squash), y: CGFloat(squash))
                    let shell = Sketch.circle(.zero, r, seed: 30 + i, wobble: 0.7)
                    nut.fill(shell, with: .color(Jungle.bark))
                    nut.pencil(shell, Jungle.ink, 1.5)
                    for e in 0..<3 {
                        let a = Double(e) * 2.1 - 1.6
                        nut.fill(Path(ellipseIn: CGRect(x: CGFloat(cos(a)) * 4 - 1.3, y: CGFloat(sin(a)) * 4 - 1.3, width: 2.6, height: 2.6)),
                                 with: .color(Jungle.ink.opacity(0.7)))
                    }
                }
            }
        }
        .accessibilityHidden(true)
    }
}

// A vine that grows leaf by leaf as the download comes in.
struct VineProgress: View, Animatable {
    var progress: Double
    var animatableData: Double {
        get { progress }
        set { progress = newValue }
    }

    var body: some View {
        Canvas { ctx, size in
            let left: CGFloat = 12, right = size.width - 12, mid = size.height / 2
            let x = left + (right - left) * CGFloat(max(0, min(1, progress)))
            let vine = Sketch.line(CGPoint(x: left, y: mid), CGPoint(x: right, y: mid), seed: 77, wobble: 2.2)
            let round = StrokeStyle(lineWidth: 6, lineCap: .round)
            ctx.stroke(vine, with: .color(Jungle.dryVine), style: round)
            var grown = ctx
            grown.clip(to: Path(CGRect(x: 0, y: 0, width: x, height: size.height)))
            grown.stroke(vine, with: .color(Jungle.vine), style: round)
            var lx = left + 18
            while lx < x - 6 {
                grown.leaf(at: CGPoint(x: lx, y: mid - 1), angle: Int(lx) % 2 == 0 ? -50 : 230, length: 13, width: 6, color: Jungle.leaf)
                lx += 26
            }
            ctx.stroke(vine, with: .color(Jungle.ink.opacity(0.6)), lineWidth: 1.2)
            let tip = Sketch.circle(CGPoint(x: x, y: mid), 7, seed: 3, wobble: 0.6)
            ctx.fill(tip, with: .color(Jungle.banana))
            ctx.pencil(tip, Jungle.ink, 1.5)
        }
    }
}

private func percent(_ v: Double) -> String { "\(Int((v * 100).rounded()))%" }

// MARK: Controls

// An on/off switch: a sketched pod with a knob that slides across and turns green.
struct LeafSwitch: View {
    @Binding var isOn: Bool
    var label = ""
    @Environment(\.isEnabled) private var enabled

    var body: some View {
        SwitchDrawing(t: isOn ? 1 : 0)
            // A soft spring, so the knob slides across and settles with a little bounce.
            .animation(.spring(response: 0.3, dampingFraction: 0.68), value: isOn)
            .frame(width: 58, height: 28)
        .opacity(enabled ? 1 : 0.4)
        .contentShape(Rectangle())
        .onTapGesture { if enabled { isOn.toggle() } }
        .accessibilityRepresentation { Toggle(label, isOn: $isOn) }
    }
}

// The switch at a point in its slide: 0 off ... 1 on (a little past either end mid-bounce).
private struct SwitchDrawing: View, Animatable {
    var t: Double
    var animatableData: Double {
        get { t }
        set { t = newValue }
    }

    var body: some View {
        Canvas { ctx, size in
            let on = CGFloat(min(1, max(0, t)))
            let pod = CGRect(origin: .zero, size: size).insetBy(dx: 1.5, dy: 1.5)
            let path = Sketch.box(pod, radius: pod.height / 2, seed: 5, wobble: 1.1)
            ctx.fill(path, with: .color(Jungle.pod))
            ctx.fill(path, with: .color(Jungle.leaf.opacity(Double(on))))
            ctx.pencil(path, Jungle.ink, 2)
            let left = pod.minX + pod.height / 2 + 1, right = pod.maxX - pod.height / 2 - 1
            let knobX = left + (right - left) * CGFloat(t)
            // The knob keeps its wobble as it slides, rather than re-shaking every frame.
            let knob = Sketch.circle(CGPoint(x: left, y: pod.midY), pod.height / 2 - 4, seed: 8, wobble: 0.8)
                .applying(CGAffineTransform(translationX: knobX - left, y: 0))
            ctx.fill(knob, with: .color(Jungle.paper))
            ctx.fill(knob, with: .color(Jungle.banana.opacity(Double(on))))
            ctx.pencil(knob, Jungle.ink, 1.8)
            if on > 0.05 {
                ctx.leaf(at: CGPoint(x: knobX - 5, y: pod.midY + 2), angle: -40, length: 13 * on, width: 6 * on,
                         color: Jungle.leafDark.opacity(Double(on)))
            }
        }
    }
}

struct VineSlider: View {
    let value: Double
    let range: ClosedRange<Double>
    var step: Double = 0
    var onDrop = false
    var label = ""
    var spoken: String?   // What VoiceOver reads for the value, like "every 1½-3 min".
    let set: (Double) -> Void
    @State private var dragging: Double?
    @Environment(\.isEnabled) private var enabled

    init(_ label: String, value: Double, range: ClosedRange<Double>, step: Double = 0, onDrop: Bool = false, spoken: String? = nil,
         set: @escaping (Double) -> Void) {
        self.label = label; self.spoken = spoken; self.value = value; self.range = range; self.step = step; self.onDrop = onDrop; self.set = set
    }

    var body: some View {
        GeometryReader { geo in
            let left: CGFloat = 12, right = geo.size.width - 12, mid = geo.size.height / 2
            let shown = dragging ?? value
            let t = CGFloat(max(0, min(1, (shown - range.lowerBound) / (range.upperBound - range.lowerBound))))
            let x = left + (right - left) * t
            Canvas { ctx, _ in
                let vine = Sketch.line(CGPoint(x: left, y: mid), CGPoint(x: right, y: mid), seed: Int(geo.size.width), wobble: 2.2)
                let round = StrokeStyle(lineWidth: 5, lineCap: .round)
                ctx.stroke(vine, with: .color(Jungle.dryVine), style: round)
                // The grown part of the vine, up to the leaf.
                var grown = ctx
                grown.clip(to: Path(CGRect(x: 0, y: 0, width: x, height: geo.size.height)))
                grown.stroke(vine, with: .color(Jungle.vine), style: round)
                var lx = left + 22
                while lx < x - 10 {
                    grown.leaf(at: CGPoint(x: lx, y: mid - 1), angle: Int(lx) % 2 == 0 ? -50 : 230, length: 12, width: 5, color: Jungle.leaf)
                    lx += 34
                }
                ctx.stroke(vine, with: .color(Jungle.ink.opacity(0.6)), lineWidth: 1.2)
                // The handle: a big leaf standing up on the vine.
                let grow: CGFloat = dragging != nil ? 1.12 : 1
                ctx.leaf(at: CGPoint(x: x, y: mid + 9 * grow), angle: -90, length: 30 * grow, width: 13 * grow, color: Jungle.leaf)
                let nub = Sketch.circle(CGPoint(x: x, y: mid), 6, seed: 3, wobble: 0.6)
                ctx.fill(nub, with: .color(Jungle.banana))
                ctx.pencil(nub, Jungle.ink, 1.5)
            }
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { g in
                    let v = valueAt(g.location.x, left: left, right: right)
                    dragging = v
                    if !onDrop { set(v) }
                }
                .onEnded { g in
                    set(valueAt(g.location.x, left: left, right: right))
                    dragging = nil
                })
        }
        .frame(height: 40)
        .opacity(enabled ? 1 : 0.4)
        .accessibilityRepresentation {
            let binding = Binding(get: { value }, set: { set(step > 0 ? ($0 / step).rounded() * step : $0) })
            if step > 0 {
                Slider(value: binding, in: range, step: step) { Text(label) }.accessibilityValue(spoken ?? "")
            } else {
                Slider(value: binding, in: range) { Text(label) }.accessibilityValue(spoken ?? "")
            }
        }
    }

    private func valueAt(_ x: CGFloat, left: CGFloat, right: CGFloat) -> Double {
        let t = Double(max(0, min(1, (x - left) / (right - left))))
        let v = range.lowerBound + (range.upperBound - range.lowerBound) * t
        return step > 0 ? (v / step).rounded() * step : v
    }
}

// A row of choices, like pebbles: the chosen one is filled in leaf green.
struct Chips: View {
    let options: [(name: String, value: String)]
    let selected: String
    let choose: (String) -> Void

    var body: some View {
        Flow(spacing: 6) {
            ForEach(options.indices, id: \.self) { i in
                let chosen = options[i].value == selected
                Text(options[i].name).font(Jungle.chip)
                    .foregroundColor(chosen ? Jungle.paper : Jungle.ink)
                    .padding(.horizontal, 9).frame(height: 30)
                    .background(Lit(on: chosen, radius: 12, seed: i * 41 + options[i].name.count, wobble: 1.3))
                    .animation(.easeOut(duration: 0.18), value: chosen)
                    .contentShape(Rectangle())
                    .onTapGesture { choose(options[i].value) }
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(options[i].name)
                    .accessibilityAddTraits(chosen ? [.isButton, .isSelected] : .isButton)
                    .accessibilityValue(chosen ? "selected" : "")
                    .accessibilityAction { choose(options[i].value) }
            }
        }
    }
}

// Lays its children out in rows, wrapping when a row is full.
struct Flow: Layout {
    var spacing: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let frames = place(subviews, width: proposal.width ?? .infinity)
        return CGSize(width: frames.map(\.maxX).max() ?? 0, height: frames.map(\.maxY).max() ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for (frame, view) in zip(place(subviews, width: bounds.width), subviews) {
            view.place(at: CGPoint(x: bounds.minX + frame.minX, y: bounds.minY + frame.minY), proposal: ProposedViewSize(frame.size))
        }
    }

    private func place(_ subviews: Subviews, width: CGFloat) -> [CGRect] {
        var frames: [CGRect] = []
        var x: CGFloat = 0, y: CGFloat = 0, rowHeight: CGFloat = 0
        for view in subviews {
            let size = view.sizeThatFits(.unspecified)
            if x > 0 && x + size.width > width { x = 0; y += rowHeight + spacing; rowHeight = 0 }
            frames.append(CGRect(origin: CGPoint(x: x, y: y), size: size))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
        return frames
    }
}


// A number with coconut buttons either side to take one away or add one.
struct CoconutStepper: View {
    let value: Int
    let range: ClosedRange<Int>
    let set: (Int) -> Void

    var body: some View {
        HStack(spacing: 0) {
            coconut(plus: false)
            Text("\(value)").font(Jungle.number).foregroundColor(Jungle.ink).frame(width: 40)
            coconut(plus: true)
        }
    }

    private func coconut(plus: Bool) -> some View {
        let enabled = plus ? value < range.upperBound : value > range.lowerBound
        return Canvas { ctx, size in
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            let shell = Sketch.circle(c, 15, seed: plus ? 2 : 1, wobble: 1)
            ctx.fill(shell, with: .color(Jungle.bark))
            ctx.pencil(shell, Jungle.ink, 2)
            var sign = Path()
            sign.move(to: CGPoint(x: c.x - 6, y: c.y)); sign.addLine(to: CGPoint(x: c.x + 6, y: c.y + 0.6))
            if plus { sign.move(to: CGPoint(x: c.x + 0.4, y: c.y - 6)); sign.addLine(to: CGPoint(x: c.x, y: c.y + 6)) }
            ctx.stroke(sign, with: .color(Jungle.paper), style: StrokeStyle(lineWidth: 3, lineCap: .round))
        }
        .frame(width: 36, height: 36)
        .opacity(enabled ? 1 : 0.35)
        .contentShape(Circle())
        .onTapGesture { if enabled { set(value + (plus ? 1 : -1)) } }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(plus ? "One more Onkey" : "One less Onkey")
        .accessibilityAddTraits(.isButton)
        .accessibilityAction { if enabled { set(value + (plus ? 1 : -1)) } }
    }
}

// A banana-yellow sign to click.
struct SignButton: View {
    let title: String
    var small = false
    let action: () -> Void
    @Environment(\.isEnabled) private var enabled
    @State private var down = false

    init(title: String, small: Bool = false, action: @escaping () -> Void) {
        self.title = title; self.small = small; self.action = action
    }

    var body: some View {
        Text(title).font(small ? Jungle.chip : Jungle.label).foregroundColor(Jungle.ink)
            .padding(.horizontal, small ? 11 : 18).frame(height: small ? 30 : 38)
            .background(SketchBox(radius: 8, seed: title.count * 13, fill: Jungle.banana, line: 2.2, wobble: 1.4))
            .offset(x: down ? 1 : 0, y: down ? 2 : 0)
            .opacity(enabled ? 1 : 0.4)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { _ in if enabled { down = true } }
                .onEnded { _ in
                    if down { action() }
                    down = false
                })
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(title)
            .accessibilityAddTraits(.isButton)
            .accessibilityAction { if enabled { action() } }
    }
}

// A quick on/off switch along the top of the panel, like a pebble that turns green.
struct QuickToggle: View {
    let title: String
    let isOn: Bool
    let flip: () -> Void

    var body: some View {
        HStack(spacing: 6) {
            Canvas { ctx, size in
                let dot = Sketch.circle(CGPoint(x: size.width / 2, y: size.height / 2), 4.5, seed: title.count, wobble: 0.5)
                ctx.fill(dot, with: .color(isOn ? Jungle.banana : Jungle.pod))
                ctx.stroke(dot, with: .color(Jungle.ink), lineWidth: 1.2)
            }
            .frame(width: 11, height: 11)
            .scaleEffect(isOn ? 1.2 : 1)
            Text(title).font(Jungle.chip).foregroundColor(isOn ? Jungle.paper : Jungle.ink)
        }
        .padding(.leading, 8).padding(.trailing, 11).frame(height: 28)
        .background(Lit(on: isOn, radius: 13, seed: title.count * 29, wobble: 1.2))
        .animation(.easeOut(duration: 0.18), value: isOn)
        .contentShape(Rectangle())
        .onTapGesture(perform: flip)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(title)
        .accessibilityValue(isOn ? "on" : "off")
        .accessibilityAddTraits(.isButton)
        .accessibilityAction { flip() }
    }
}

// A sketched pebble that fades from paper to leaf green as it's turned on.
struct Lit: View {
    let on: Bool
    let radius: CGFloat
    let seed: Int
    var wobble: CGFloat = 1.3

    var body: some View {
        ZStack {
            SketchBox(radius: radius, seed: seed, fill: Jungle.paper, line: 1.6, wobble: wobble)
            SketchBox(radius: radius, seed: seed, fill: Jungle.leaf, line: 2.3, wobble: wobble).opacity(on ? 1 : 0)
        }
    }
}
