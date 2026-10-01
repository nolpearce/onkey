import AppKit

enum Key {
    static let zone = "zone", chase = "chase", speed = "speed"
    static let soundOn = "soundOn", soundGap = "soundGap", volume = "volume"
    static let size = "size", opacity = "opacity", layer = "layer", draggable = "draggable"
    static let watchCursor = "watchCursor", blink = "blink"
    static let count = "count", dance = "dance"
    static let checkUpdates = "checkUpdates"
    static let savedX = "savedX", savedY = "savedY"
}

// Settings read once whenever one changes, instead of from UserDefaults on every tick
// (those reads were the single biggest cost with many Onkeys).
struct Prefs {
    var zone = "anywhere", chase = 5, speed = 42.0
    var soundOn = true, soundGap = 90.0, volume = 1.0
    var size: CGFloat = 1, watchCursor = true, blink = true, dance = false

    init() {}
    init(_ d: UserDefaults) {
        zone = d.string(forKey: Key.zone) ?? "anywhere"
        chase = d.integer(forKey: Key.chase)
        speed = d.double(forKey: Key.speed)
        soundOn = d.bool(forKey: Key.soundOn)
        soundGap = d.double(forKey: Key.soundGap)
        volume = d.double(forKey: Key.volume)
        size = CGFloat(d.double(forKey: Key.size))
        watchCursor = d.bool(forKey: Key.watchCursor)
        blink = d.bool(forKey: Key.blink)
        dance = d.bool(forKey: Key.dance)
    }
}

final class OptionTag: NSObject {
    let key: String, value: NSObject
    init(_ key: String, _ value: NSObject) { self.key = key; self.value = value }
}
