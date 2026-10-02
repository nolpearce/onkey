import AppKit
import CryptoKit

// Checks GitHub Releases for a newer Onkey and installs it in place: downloads
// Onkey-Mac.zip, checks it against the SHA-256 GitHub lists for it, unzips it beside the
// running app, then quits and lets a tiny shell script swap the app and reopen it. It never
// pops up a window: the Updates tab of the settings panel shows what it's doing (looking,
// what's new, download progress, or what went wrong), so the whole update happens there.
// Windows/Source/Updater.cs does the same for the Windows version.
final class Updater {
    struct Release {
        let version: String
        let download: URL
        let sha256: String?
        let notes: String
    }

    enum Phase: Equatable {
        case idle            // Hasn't looked yet.
        case checking
        case upToDate
        case available       // `available` holds the release.
        case downloading     // `progress` runs from 0 to 1.
        case restarting
        case failed(String)
    }

    static let latestURL = URL(string: "https://api.github.com/repos/nolpearce/onkey/releases/latest")!
    static let assetName = "Onkey-Mac.zip"
    static var current: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    // A newer release, once a check has found one.
    private(set) var available: Release?
    private(set) var phase = Phase.idle
    private(set) var progress = 0.0
    private(set) var lastChecked: Date?
    var downloading: Bool { phase == .downloading || phase == .restarting }
    // Called on the main thread whenever any of the above changes.
    var onChange: (() -> Void)?
    private var timer: Timer?
    private var progressWatch: NSKeyValueObservation?

    // Checks shortly after launch and then every six hours, quietly.
    func startAutomaticChecks() {
        guard timer == nil else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 15) { [weak self] in
            guard let self, self.timer != nil else { return }
            self.check()
        }
        let t = Timer(timeInterval: 6 * 60 * 60, repeats: true) { [weak self] _ in self?.check() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stopAutomaticChecks() {
        timer?.invalidate()
        timer = nil
    }

    // Looks again when the Updates tab opens, unless it looked in the last minute.
    func checkIfStale() {
        if phase == .idle || (phase != .checking && !downloading && Date().timeIntervalSince(lastChecked ?? .distantPast) > 60) {
            check()
        }
    }

    // Asks GitHub for the latest release. The answer shows in the Updates tab; the search
    // shows for at least a moment, so the loading animation never just flickers.
    func check() {
        guard phase != .checking, !downloading else { return }
        set(.checking)
        let started = Date()
        var request = URLRequest(url: Self.latestURL, timeoutInterval: 20)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("Onkey/\(Self.current)", forHTTPHeaderField: "User-Agent")
        URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            let result: Result<Release?, Error>
            if let error {
                result = .failure(error)
            } else if let data, let release = Self.parse(data) {
                result = .success(Self.isNewer(release.version, than: Self.current) ? release : nil)
            } else {
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                result = .failure(UpdateError("GitHub answered with something Onkey didn't understand (HTTP \(status))."))
            }
            let wait = max(0, 0.9 - Date().timeIntervalSince(started))
            DispatchQueue.main.asyncAfter(deadline: .now() + wait) { self?.checked(result) }
        }.resume()
    }

    private func checked(_ result: Result<Release?, Error>) {
        lastChecked = Date()
        switch result {
        case .success(let release):
            if let release, available?.version != release.version { Log.info("Onkey \(release.version) is available") }
            available = release
            set(release == nil ? .upToDate : .available)
        case .failure(let error):
            Log.error("Checking for updates", error)
            // Keep showing a release already found; otherwise say what went wrong.
            set(available != nil ? .available : .failed("Couldn't reach GitHub. \(error.localizedDescription)"))
        }
    }

    private func set(_ new: Phase) {
        if case .failed(let problem) = new { Log.warn("Update failed: \(problem)") }
        phase = new
        onChange?()
    }

    // Where the new Onkey.app goes: over this one, unless macOS is running him from a
    // read-only copy (App Translocation, when he's opened straight from Downloads) or his
    // folder can't be written, in which case he moves into Applications.
    static func installTarget() -> URL {
        let app = Bundle.main.bundleURL
        let fm = FileManager.default
        if !app.path.contains("/AppTranslocation/"), fm.isWritableFile(atPath: app.deletingLastPathComponent().path) { return app }
        let system = URL(fileURLWithPath: "/Applications", isDirectory: true)
        if fm.isWritableFile(atPath: system.path) { return system.appendingPathComponent("Onkey.app") }
        let home = fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications", isDirectory: true)
        try? fm.createDirectory(at: home, withIntermediateDirectories: true)
        return home.appendingPathComponent("Onkey.app")
    }

    // MARK: Installing

    // Downloads and installs the release the last check found, then restarts.
    func install() {
        guard let release = available, !downloading else { return }
        let app = Self.installTarget()
        guard FileManager.default.isWritableFile(atPath: app.deletingLastPathComponent().path) else {
            set(.failed("Onkey can't write to \(app.deletingLastPathComponent().path). Drag Onkey.app into Applications, open him from there, and try again."))
            return
        }
        Log.info("Updating to \(release.version) from \(release.download.absoluteString) into \(app.path)")
        progress = 0
        set(.downloading)
        let task = URLSession.shared.downloadTask(with: release.download) { [weak self] file, response, error in
            let outcome: Result<URL, Error>
            let status = (response as? HTTPURLResponse)?.statusCode ?? 200
            if let file, status == 200 {
                outcome = Result { try Self.unpack(file, release: release, beside: app) }
            } else if file != nil {
                outcome = .failure(UpdateError("GitHub answered the download with HTTP \(status). Try again later."))
            } else {
                outcome = .failure(error ?? UpdateError("The download didn't finish."))
            }
            DispatchQueue.main.async {
                guard let self else { return }
                self.progressWatch = nil
                switch outcome {
                case .success(let newApp):
                    Log.info("Downloaded and unpacked into \(newApp.path)")
                    self.progress = 1
                    self.set(.restarting)
                    // A beat to show "Restarting", then swap.
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { self.relaunch(replacing: app, with: newApp) }
                case .failure(let error):
                    Log.error("Downloading the update", error)
                    self.set(.failed("The update didn't work. \(error.localizedDescription)"))
                }
            }
        }
        progressWatch = task.progress.observe(\.fractionCompleted) { [weak self] p, _ in
            let done = p.fractionCompleted
            DispatchQueue.main.async {
                guard let self, self.phase == .downloading, done - self.progress > 0.01 || done >= 1 else { return }
                self.progress = done
                self.onChange?()
            }
        }
        task.resume()
    }

    // Checks the download and unzips it into a scratch folder on the same disk as the app,
    // returning the new Onkey.app. Runs off the main thread.
    private static func unpack(_ zip: URL, release: Release, beside app: URL) throws -> URL {
        if release.sha256 == nil { Log.warn("The release lists no checksum for \(assetName), so it isn't checked") }
        if let expected = release.sha256 {
            let digest = SHA256.hash(data: try Data(contentsOf: zip)).map { String(format: "%02x", $0) }.joined()
            guard digest == expected.lowercased() else {
                throw UpdateError("The download was damaged (its checksum didn't match). Try again later.")
            }
        }
        let scratch = try FileManager.default.url(for: .itemReplacementDirectory, in: .userDomainMask,
                                                  appropriateFor: app.deletingLastPathComponent(), create: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, scratch.path])
        let newApp = scratch.appendingPathComponent("Onkey.app")
        guard FileManager.default.fileExists(atPath: newApp.appendingPathComponent("Contents/MacOS/Onkey").path) else {
            throw UpdateError("The download didn't contain Onkey.app.")
        }
        // Downloads made by Onkey aren't quarantined, but make sure macOS won't block the new copy.
        try? run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", newApp.path])
        return newApp
    }

    // Quits, and once Onkey has fully exited swaps the new app into place (or moves it in,
    // when he's moving into Applications) and opens it. If the swap fails the old app is put
    // back and reopened instead. If Onkey hasn't gone after 30 seconds he's stopped. Each
    // step goes in Onkey's log.
    private func relaunch(replacing app: URL, with newApp: URL) {
        let script = """
            log() { echo "$(date '+%Y-%m-%d %H:%M:%S.000') [$$] INFO [install] $*" >> "$4"; }
            log "Waiting for Onkey to exit"
            n=0
            while kill -0 "$1" 2>/dev/null; do
                sleep 0.2; n=$((n + 1))
                if [ $n -eq 150 ]; then log "Onkey hasn't exited after 30 seconds; stopping him"; kill -9 "$1" 2>/dev/null; fi
            done
            rm -rf "$3.old"
            if [ ! -e "$3" ]; then mv "$2" "$3" && log "Moved the new Onkey into $3" || log "Couldn't move the new Onkey into $3"
            elif mv "$3" "$3.old" && mv "$2" "$3"; then rm -rf "$3.old"; log "Swapped in the new Onkey"
            else [ -d "$3" ] || mv "$3.old" "$3"; log "Couldn't swap in the new Onkey; kept the old one"; fi
            rm -rf "$(dirname "$2")"
            open "$3" && log "Opened $3" || log "Couldn't open $3"
            """
        let swap = Process()
        swap.executableURL = URL(fileURLWithPath: "/bin/sh")
        swap.arguments = ["-c", script, "onkey-update", String(getpid()), newApp.path, app.path, Log.fileURL.path]
        do {
            try swap.run()
        } catch {
            Log.error("Starting the swap", error)
            set(.failed("The update didn't work. \(error.localizedDescription)"))
            return
        }
        Log.info("Handed over to the swap; quitting")
        // The swap waits for this Onkey to quit, so if quitting gets stuck, leave anyway.
        DispatchQueue.global().asyncAfter(deadline: .now() + 8) {
            Log.warn("Onkey was slow to quit for the update, so he left without tidying up")
            Log.flush()
            exit(0)
        }
        NSApp.terminate(nil)
    }

    // MARK: Helpers

    private static func parse(_ data: Data) -> Release? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = json["tag_name"] as? String,
              let assets = json["assets"] as? [[String: Any]],
              let asset = assets.first(where: { $0["name"] as? String == assetName }),
              let download = (asset["browser_download_url"] as? String).flatMap({ URL(string: $0) }) else { return nil }
        let digest = (asset["digest"] as? String).flatMap { $0.hasPrefix("sha256:") ? String($0.dropFirst(7)) : nil }
        let version = tag.hasPrefix("v") || tag.hasPrefix("V") ? String(tag.dropFirst()) : tag
        return Release(version: version, download: download, sha256: digest,
                       notes: plainNotes(json["body"] as? String ?? ""))
    }

    // The release notes without their Markdown, for the Updates tab.
    static func plainNotes(_ markdown: String) -> String {
        var lines: [String] = []
        for raw in markdown.replacingOccurrences(of: "\r", with: "").split(separator: "\n", omittingEmptySubsequences: false) {
            var line = raw.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "**", with: "")
            while line.hasPrefix("#") { line.removeFirst() }
            line = line.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("- ") || line.hasPrefix("* ") { line = "• " + line.dropFirst(2) }
            if line.isEmpty, lines.last?.isEmpty ?? true { continue }
            lines.append(line)
        }
        var text = lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        if text.count > 4000 { text = String(text.prefix(4000)).trimmingCharacters(in: .whitespacesAndNewlines) + "…" }
        return text
    }

    // Compares dotted version numbers, so 4.10 is newer than 4.9 and 4.3 equals 4.3.0.
    static func isNewer(_ a: String, than b: String) -> Bool {
        func parts(_ s: String) -> [Int] { s.split(separator: ".").map { Int($0.prefix(while: \.isNumber)) ?? 0 } }
        let x = parts(a), y = parts(b)
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }

    private static func run(_ tool: String, _ arguments: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = arguments
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw UpdateError("\(tool) failed (exit \(p.terminationStatus)).") }
    }
}

struct UpdateError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}
