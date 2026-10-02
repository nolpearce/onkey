import AppKit
import Darwin

// Onkey's diary, so a crash can be looked into: what he did and what went wrong, in
// ~/Library/Logs/Onkey/onkey.log (Console.app shows it too). It keeps about the last half
// megabyte (the older half moves to onkey.old.log). When he crashes, crash.txt records it
// until a crash report is sent, so the settings panel can offer one. Nothing here throws:
// a log that can't be written is just skipped. Windows/Source/Log.cs does the same.
enum Log {
    static let folder = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/Onkey", isDirectory: true)
    static let fileURL = folder.appendingPathComponent("onkey.log")
    static let crashURL = folder.appendingPathComponent("crash.txt")
    private static let oldURL = folder.appendingPathComponent("onkey.old.log")
    private static let maxSize = 512 * 1024
    // Every write happens here, one at a time.
    private static let queue = DispatchQueue(label: "local.onkey.log")
    // How often each error has been seen, so one that repeats every frame logs a few times
    // and then only now and then. Only touched on `queue`.
    private static var seen: [String: Int] = [:]
    private static let stamp: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        return f
    }()

    static func start() {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        info("Onkey \(Updater.current) starting from \(Bundle.main.bundlePath) on \(systemVersion)")
        installCrashHandlers()
    }

    static var systemVersion: String {
        #if arch(arm64)
        let chip = "Apple silicon"
        #else
        let chip = "Intel"
        #endif
        return "macOS \(ProcessInfo.processInfo.operatingSystemVersionString), \(chip)"
    }

    static func info(_ message: String) { queue.async { append("INFO", message) } }
    // Waits for everything logged so far to be written, before quitting.
    static func flush() { queue.sync {} }
    static func warn(_ message: String) { queue.async { append("WARN", message) } }

    static func error(_ what: String, _ error: Error? = nil) {
        let message = error.map { "\(what): \($0.localizedDescription)" } ?? what
        queue.async {
            let count = (seen[message] ?? 0) + 1
            seen[message] = count
            if count <= 3 { append("ERROR", message) } else if count % 100 == 0 { append("ERROR", "\(message) (seen \(count) times)") }
        }
    }

    // Something went badly wrong: logged, and kept in crash.txt for a crash report.
    static func crash(_ what: String, details: String) {
        queue.sync {
            append("CRASH", "\(what)\n\(details)")
            let text = "Onkey \(Updater.current): \(what)\n\(details)\n"
            try? text.write(to: crashURL, atomically: true, encoding: .utf8)
        }
    }

    // What crash.txt says, or nil when there's no crash waiting to be reported.
    static var pendingCrash: String? { try? String(contentsOf: crashURL, encoding: .utf8) }
    static var hasCrash: Bool { FileManager.default.fileExists(atPath: crashURL.path) }
    static func clearCrash() { try? FileManager.default.removeItem(at: crashURL) }

    // The end of the log (the old half too, if needed), up to the given length.
    static func tail(_ maxChars: Int) -> String {
        let text = queue.sync {
            [oldURL, fileURL].compactMap { try? String(contentsOf: $0, encoding: .utf8) }.joined()
        }
        guard text.count > maxChars else { return text }
        let tail = text.suffix(maxChars)
        if let line = tail.firstIndex(of: "\n"), tail.index(after: line) < tail.endIndex { return String(tail[tail.index(after: line)...]) }
        return String(tail)
    }

    // Only called on `queue`.
    private static func append(_ level: String, _ message: String) {
        let line = "\(stamp.string(from: Date())) [\(getpid())] \(level) "
            + message.replacingOccurrences(of: "\n", with: "\n    ") + "\n"
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        if let size = (try? fm.attributesOfItem(atPath: fileURL.path))?[.size] as? Int, size > maxSize {
            try? fm.removeItem(at: oldURL)
            try? fm.moveItem(at: fileURL, to: oldURL)
        }
        guard let data = line.data(using: .utf8) else { return }
        // The throwing calls, since the older ones raise an exception when the disk is full.
        if let handle = try? FileHandle(forWritingTo: fileURL) {
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: data)
            try? handle.close()
        } else {
            try? data.write(to: fileURL)
        }
    }

    // MARK: Crashes

    // A Swift error like a missing value stops the program with a signal, before any Swift
    // code could catch it. These handlers write what they can (which signal, and where in the
    // code) using only calls that are safe at that moment, then let macOS's own crash report
    // carry on as normal.
    private static func installCrashHandlers() {
        crashFilePath = strdup(crashURL.path)
        logFilePath = strdup(fileURL.path)
        crashHeader = strdup("Onkey \(Updater.current) crashed (")
        crashFrames = UnsafeMutablePointer<UnsafeMutableRawPointer?>.allocate(capacity: 64)
        for s in [SIGSEGV, SIGBUS, SIGILL, SIGTRAP, SIGABRT, SIGFPE] { signal(s, onCrashSignal) }
        NSSetUncaughtExceptionHandler { exception in
            crashRecorded = true   // The abort that follows mustn't write over this.
            Log.crash("Onkey crashed: \(exception.name.rawValue): \(exception.reason ?? "")",
                      details: exception.callStackSymbols.joined(separator: "\n"))
        }
    }
}

// Set up before any crash, since a signal handler mustn't allocate memory.
private var crashFilePath: UnsafeMutablePointer<CChar>?
private var logFilePath: UnsafeMutablePointer<CChar>?
private var crashHeader: UnsafeMutablePointer<CChar>?
private var crashFrames: UnsafeMutablePointer<UnsafeMutableRawPointer?>?
private var crashRecorded = false

private func onCrashSignal(_ sig: Int32) {
    let name: StaticString
    switch sig {
    case SIGSEGV: name = "SIGSEGV, bad memory access"
    case SIGBUS: name = "SIGBUS, bad memory access"
    case SIGILL: name = "SIGILL"
    case SIGTRAP: name = "SIGTRAP, a Swift runtime error"
    case SIGABRT: name = "SIGABRT, aborted"
    case SIGFPE: name = "SIGFPE, arithmetic error"
    default: name = "a signal"
    }
    let count = crashFrames.map { backtrace($0, 64) } ?? 0
    if !crashRecorded { dumpCrash(to: crashFilePath, flags: O_WRONLY | O_CREAT | O_TRUNC, prefix: "", name, count) }
    dumpCrash(to: logFilePath, flags: O_WRONLY | O_CREAT | O_APPEND, prefix: "CRASH ", name, count)
    signal(sig, SIG_DFL)
    raise(sig)
}

private func dumpCrash(to path: UnsafeMutablePointer<CChar>?, flags: Int32, prefix: StaticString, _ name: StaticString, _ count: Int32) {
    guard let path else { return }
    let fd = open(path, flags, 0o644)
    guard fd >= 0 else { return }
    writeAll(fd, prefix)
    if let header = crashHeader { _ = write(fd, header, strlen(header)) }
    writeAll(fd, name)
    writeAll(fd, ")\n")
    if let frames = crashFrames, count > 0 { backtrace_symbols_fd(frames, count, fd) }
    close(fd)
}

private func writeAll(_ fd: Int32, _ text: StaticString) {
    _ = write(fd, text.utf8Start, text.utf8CodeUnitCount)
}

// A crash report: Onkey's version, macOS's, what crashed and the end of the log, put on the
// clipboard and into a new GitHub issue that opens in the browser for checking before it's
// sent. Nothing is sent by Onkey itself.
enum CrashReport {
    private static let newIssue = "https://github.com/nolpearce/onkey/issues/new"

    static var pending: Bool { Log.hasCrash }

    static func send() {
        let crash = Log.pendingCrash.map(hidingHome)
        Log.info("Sending a crash report")
        let details = "Onkey \(Updater.current) on \(Log.systemVersion)"
        let full = hidingHome("\(details)\n\n" + (crash.map { "What went wrong:\n\($0)\n" } ?? "") + "Log:\n" + Log.tail(60000))
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(full, forType: .string)

        // A link only holds so much (GitHub turns away ones much over 8,000 characters), so
        // the issue gets the crash and as much of the end of the log as fits; the whole log
        // is on the clipboard.
        let firstLine = crash.flatMap { $0.split(separator: "\n").first.map(String.init) } ?? ""
        let title = crash != nil ? "Crash report: " + cut(firstLine, 80) : "Problem report"
        var link = ""
        for tail in stride(from: 2400, through: 0, by: -300) {
            var body = "**What happened?**\n(What was Onkey doing? Were you updating him?)\n\n**Version:** \(details)\n"
            if let crash { body += "\n**What went wrong**\n```\n\(cut(crash, 1200))\n```\n" }
            if tail > 0 { body += "\n**The end of the log**\n```\n\(hidingHome(Log.tail(tail)))\n```\n" }
            body += "\nThe whole log is on your clipboard: paste it below if it helps.\n"
            link = newIssue + "?title=" + encode(title) + "&body=" + encode(body)
            if link.count <= 7000 { break }
        }
        if let url = URL(string: link), NSWorkspace.shared.open(url) {
            Log.clearCrash()
        } else {
            Log.error("Opening the crash report")
            // No browser: the report is on the clipboard, so show where the log is.
            NSWorkspace.shared.activateFileViewerSelecting([Log.fileURL])
        }
    }

    // The user's own folder name appears in paths; leave it out.
    private static func hidingHome(_ text: String) -> String {
        let home = NSHomeDirectory()
        return home.count > 1 ? text.replacingOccurrences(of: home, with: "~") : text
    }

    private static func encode(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: CharacterSet(charactersIn:
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~")) ?? ""
    }

    private static func cut(_ s: String, _ max: Int) -> String { s.count <= max ? s : String(s.prefix(max)) + "..." }
}
