import AppKit
import AVFoundation
import ServiceManagement

final class OnkeyApp: NSObject, NSApplicationDelegate {
    let defaults = UserDefaults.standard
    private(set) var renderer: OnkeyRenderer!
    private var statusItem: NSStatusItem!
    private var pauseItem: NSMenuItem!
    private var updateItem: NSMenuItem!
    // Finds and installs new releases from GitHub.
    let updater = Updater()
    private var optionItems: [NSMenuItem] = []
    private var menu: NSMenu!
    private var settingsPanel: SettingsPanel?
    // Called whenever a setting or the update state changes, so an open Settings window can follow.
    var onSettingsChanged: (() -> Void)?
    private var pets: [Pet] = []
    private(set) var frames: [CGImage] = []
    private(set) var idle: CGImage!
    // The lowest opaque row of each frame (points, y-down, unscaled): where his hands end.
    private(set) var frameBottoms: [CGFloat] = []
    private(set) var idleBottom = OnkeyRenderer.canvas.height
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
    // Hears music and finds its beat while "Dance to Music" is on.
    private let listener = MusicListener()
    private var danceLevel = 0.0   // Eases between 0 (still) and 1 (bopping) as music starts and stops.
    private var heardBeat = false
    private(set) var prefs = Prefs()
    var watching: Bool { prefs.watchCursor }

    var size: CGFloat { prefs.size }
    var windowSize: NSSize {
        NSSize(width: OnkeyRenderer.canvas.width * size, height: OnkeyRenderer.canvas.height * size)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        defaults.register(defaults: [
            Key.zone: "anywhere", Key.chase: 5, Key.speed: 42.0,
            Key.soundOn: true, Key.soundGap: 90.0, Key.volume: 1.0,
            Key.size: 1.0, Key.opacity: 1.0, Key.layer: "above", Key.overFullScreen: true, Key.draggable: false, Key.watchCursor: true, Key.blink: true,
            Key.count: 1, Key.dance: false, Key.checkUpdates: true,
        ])
        prefs = Prefs(defaults)
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
        if prefs.dance { startListening() }
        updater.onChange = { [weak self] in self?.refreshMenu() }
        if defaults.bool(forKey: Key.checkUpdates) { updater.startAutomaticChecks() }

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in self?.screensChanged() }

        lastTick = ProcessInfo.processInfo.systemUptime
        let t = Timer(timeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in self?.tick() }
        t.tolerance = 1.0 / 120   // Lets macOS line the ticks up with other work to save power.
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
        var rendered: [CGImage] = [], bottoms: [CGFloat] = []
        for i in 0..<OnkeyRenderer.frameCount {
            let phase = Double(i) / Double(OnkeyRenderer.frameCount) * 2 * .pi
            guard let f = renderer.render(phase: phase, walking: true, pixelsPerPoint: pixelScale,
                                          blankEyes: watching) else { continue }
            rendered.append(f.image)
            bottoms.append(f.opaqueBounds.maxY)
            bounds = bounds.union(f.opaqueBounds)
        }
        guard let still = renderer.render(phase: 0, walking: false, pixelsPerPoint: pixelScale,
                                          blankEyes: watching) else { return }
        idle = still.image
        idleBottom = still.opaqueBounds.maxY
        frames = rendered.isEmpty ? [still.image] : rendered
        frameBottoms = rendered.isEmpty ? [idleBottom] : bottoms
        petBounds = bounds.union(still.opaqueBounds)
    }

    private func menuIcon() -> NSImage { headImage(height: 18) }

    // Onkey's head, cropped from the top of his opaque area.
    func headImage(height: CGFloat) -> NSImage {
        let k = max(4, (height / 18).rounded(.up) * 4)
        guard let f = renderer.render(phase: 0, walking: false, pixelsPerPoint: k),
              let cropped = f.image.cropping(to: f.opaqueBounds.applying(CGAffineTransform(scaleX: k, y: k)).integral)
        else { return NSImage() }
        let width = height * CGFloat(cropped.width) / CGFloat(cropped.height)
        return NSImage(cgImage: cropped, size: NSSize(width: width, height: height))
    }

    // MARK: Menu

    // The menu keeps only the quick actions; everything else is in the Settings window.
    private func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = menuIcon()
        statusItem.button?.toolTip = "Onkey"
        // A click opens the settings panel; a right-click (or Control-click) opens this short menu.
        statusItem.button?.target = self
        statusItem.button?.action = #selector(statusItemClicked)
        statusItem.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])

        menu = NSMenu()
        menu.autoenablesItems = false   // So the update item can grey out while downloading.
        pauseItem = item("Pause Onkey", #selector(togglePause), "p")
        menu.addItem(pauseItem)
        menu.addItem(option("Dance to Music", Key.dance))
        menu.addItem(option("Let Me Drag Onkey Around", Key.draggable))
        menu.addItem(.separator())
        // Only shown while there's an update to install.
        updateItem = item("Update Onkey…", #selector(checkForUpdates), "u")
        menu.addItem(updateItem)
        menu.addItem(item("Settings…", #selector(showSettings), ","))
        menu.addItem(item("Quit Onkey", #selector(quit), "q"))
        refreshMenu()
    }

    private func item(_ title: String, _ action: Selector, _ key: String) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: key)
        i.target = self
        return i
    }

    // An on/off setting.
    private func option(_ title: String, _ key: String) -> NSMenuItem {
        let i = item(title, #selector(toggleOption(_:)), "")
        i.representedObject = key
        optionItems.append(i)
        return i
    }

    @objc private func toggleOption(_ sender: NSMenuItem) {
        guard let key = sender.representedObject as? String else { return }
        change(key, to: !defaults.bool(forKey: key))
    }

    @objc private func statusItemClicked() {
        guard let button = statusItem.button else { return }
        // VoiceOver's press arrives with no mouse event; treat it as a click.
        let event = NSApp.currentEvent
        if event?.type == .rightMouseUp || (event?.type == .leftMouseUp && event?.modifierFlags.contains(.control) == true) {
            settingsPanel?.close()
            statusItem.menu = menu
            button.performClick(nil)
            statusItem.menu = nil
        } else if settingsPanel?.isShown == true {
            settingsPanel?.close()
        } else {
            showSettings()
        }
    }

    // Hangs the settings panel from the menu bar icon.
    @objc func showSettings() {
        guard let button = statusItem.button else { return }
        if settingsPanel == nil {
            settingsPanel = SettingsPanel(app: self)
            settingsPanel?.onClose = { [weak button] in button?.highlight(false) }
        }
        settingsPanel?.show(below: button)
        button.highlight(true)
    }

    // Stores a setting and puts it into effect straight away.
    func change(_ key: String, to value: Any) {
        if let old = defaults.object(forKey: key) as? NSObject, let new = value as? NSObject, old.isEqual(new) { return }
        let oldWindowSize = windowSize
        defaults.set(value, forKey: key)
        prefs = Prefs(defaults)
        switch key {
        case Key.count:
            matchPetCount()
        case Key.dance:
            if prefs.dance { startListening() } else { listener.stop() }
        case Key.size:
            renderFrames()
            pets.forEach { $0.resize(from: oldWindowSize) }
        case Key.watchCursor:
            renderFrames()
            pets.forEach { $0.framesChanged(); $0.updateFace(dt: 1) }
        case Key.opacity, Key.layer, Key.draggable, Key.overFullScreen:
            pets.forEach { $0.applyAppearance() }
        case Key.zone, Key.chase:
            pets.forEach { $0.restFor(0); $0.pickTarget() }
        case Key.soundGap:
            pets.forEach { $0.rescheduleSound() }
        case Key.soundOn:
            if !defaults.bool(forKey: Key.soundOn) { pets.forEach { $0.stopSound() } }
        case Key.checkUpdates:
            if defaults.bool(forKey: Key.checkUpdates) { updater.startAutomaticChecks() } else { updater.stopAutomaticChecks() }
        default:
            break
        }
        refreshMenu()
    }

    private func refreshMenu() {
        for i in optionItems {
            guard let key = i.representedObject as? String else { continue }
            i.state = defaults.bool(forKey: key) ? .on : .off
        }
        if let release = updater.available {
            updateItem.isHidden = false
            updateItem.title = updater.downloading ? "Downloading Onkey \(release.version)…" : "Update to Onkey \(release.version)…"
        } else {
            updateItem.isHidden = true
        }
        updateItem.isEnabled = !updater.downloading
        statusItem.button?.toolTip = updater.available.map { "Onkey (version \($0.version) is available)" } ?? "Onkey"
        onSettingsChanged?()
    }

    var hasSound: Bool { baseSound != nil }

    var opensAtLogin: Bool {
        if #available(macOS 13, *) { return SMAppService.mainApp.status == .enabled }
        return false
    }

    // MARK: Ticking

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        let dt = min(0.08, max(0, now - lastTick))
        lastTick = now
        if !paused { clock += dt }
        // One Core Animation commit for every Onkey's changes, rather than one each.
        let squash = beatSquash(now: now, dt: dt)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        for pet in pets {
            if !paused && !pet.dragging { pet.walk(dt: dt) }
            pet.updateFace(dt: dt)
            pet.bop(squash)
        }
        CATransaction.commit()
    }

    // How squashed every Onkey is right now. He squashes down just before each beat so the
    // deepest point (15%) lands exactly on it, then springs back more slowly; faded in and
    // out as music starts and stops.
    private func beatSquash(now: Double, dt: Double) -> CGFloat {
        let beat = listener.current
        if beat.active != heardBeat {
            heardBeat = beat.active
            NSLog("Onkey: %@", beat.active ? String(format: "dancing to %.0f BPM", 60 / beat.period) : "music stopped")
        }
        let target = prefs.dance && beat.active && !paused ? 1.0 : 0.0
        danceLevel += (target - danceLevel) * min(1, dt * 3)
        guard danceLevel > 0.001, beat.period > 0 else { return 0 }
        var phase = ((now - beat.lastBeat) / beat.period).truncatingRemainder(dividingBy: 1)
        if phase < 0 { phase += 1 }
        return CGFloat(0.15 * danceLevel * Self.bopShape(phase))
    }

    // 0...1 over one beat, peaking at 1 on the beat (phase 0): eases down over the last 20%
    // of the beat before, and back up over the first 45% after.
    static func bopShape(_ phase: Double) -> Double {
        let fromBeat = phase < 0.5 ? phase : phase - 1   // Negative before the beat.
        let width = fromBeat < 0 ? 0.2 : 0.45
        guard abs(fromBeat) < width else { return 0 }
        return 0.5 + 0.5 * cos(Double.pi * fromBeat / width)
    }

    private func startListening() {
        do {
            try listener.start()
        } catch {
            listener.stop()
            defaults.set(false, forKey: Key.dance)
            prefs = Prefs(defaults)
            refreshMenu()
            let alert = NSAlert()
            alert.messageText = "Onkey can't hear your music"
            alert.informativeText = "\(error.localizedDescription)\n\nIf you said no to the permission prompt, allow Onkey under System Settings > Privacy & Security > Screen & System Audio Recording (or Microphone on older macOS), then turn Dance to Music back on."
            NSApp.activate(ignoringOtherApps: true)
            alert.runModal()
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

    var isPaused: Bool { paused }

    @objc func togglePause() {
        paused.toggle()
        pauseItem.title = paused ? "Resume Onkey" : "Pause Onkey"
        if paused { pets.forEach { $0.stopSound(); $0.present(idle) } }
        onSettingsChanged?()
    }

    // Every Onkey says it at once.
    @objc func playNow() { pets.forEach { $0.playSound(force: true) } }

    // Gathers every Onkey onto the screen under the mouse, loosely around the middle.
    @objc func bringHere() {
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

    @objc func toggleLogin() {
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

    // The menu's update item opens the panel on its Updates tab, where the update happens.
    @objc func checkForUpdates() {
        showSettings()
        settingsPanel?.showTab(SettingsView.updatesTab)
    }

    @objc func quit() { listener.stop(); NSApp.terminate(nil) }

    private func fail(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "Onkey could not start"
        alert.informativeText = message
        alert.runModal()
        NSApp.terminate(nil)
    }
}

// `Onkey --export <folder>` writes the rendered frames as PNGs, for checking the renderer.
