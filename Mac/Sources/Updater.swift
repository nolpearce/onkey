import AppKit
import CryptoKit

// Checks GitHub Releases for a newer Onkey and installs it in place: downloads
// Onkey-Mac.zip, checks it against the SHA-256 GitHub lists for it, unzips it beside the
// running app, then quits and lets a tiny shell script swap the app and reopen it.
// Windows/Source/Updater.cs does the same for the Windows version.
final class Updater {
    struct Release {
        let version: String
        let download: URL
        let sha256: String?
        let page: URL
    }

    static let latestURL = URL(string: "https://api.github.com/repos/nolpearce/onkey/releases/latest")!
    static let assetName = "Onkey-Mac.zip"
    static var current: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    // A newer release, once a check has found one.
    private(set) var available: Release?
    private(set) var downloading = false
    // Called on the main thread whenever `available` or `downloading` changes.
    var onChange: (() -> Void)?
    private var timer: Timer?

    // Checks shortly after launch and then every six hours, quietly.
    func startAutomaticChecks() {
        guard timer == nil else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 15) { [weak self] in
            guard let self, self.timer != nil else { return }
            self.check(userAsked: false)
        }
        let t = Timer(timeInterval: 6 * 60 * 60, repeats: true) { [weak self] _ in self?.check(userAsked: false) }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stopAutomaticChecks() {
        timer?.invalidate()
        timer = nil
    }

    // The menu item: installs a release already found, or looks for one.
    func menuChosen() {
        if available != nil { offerInstall() } else { check(userAsked: true) }
    }

    // Asks GitHub for the latest release. Quiet checks only update the menu; when the user
    // asked, they also hear "you're up to date" or what went wrong.
    func check(userAsked: Bool) {
        guard !downloading else { return }
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
            DispatchQueue.main.async { self?.checked(result, userAsked: userAsked) }
        }.resume()
    }

    private func checked(_ result: Result<Release?, Error>, userAsked: Bool) {
        switch result {
        case .success(let release):
            available = release
            onChange?()
            if userAsked {
                if release != nil { offerInstall() } else { say("You have the latest Onkey", "Version \(Self.current) is the newest one.") }
            }
        case .failure(let error):
            if userAsked { say("Couldn't check for updates", error.localizedDescription) }
        }
    }

    private func offerInstall() {
        guard let release = available else { return }
        let alert = NSAlert()
        alert.messageText = "Onkey \(release.version) is out"
        alert.informativeText = "You have \(Self.current). Onkey will download the new version, restart, and keep all your settings."
        alert.addButton(withTitle: "Update and Restart")
        alert.addButton(withTitle: "What's New")
        alert.addButton(withTitle: "Later")
        NSApp.activate(ignoringOtherApps: true)
        switch alert.runModal() {
        case .alertFirstButtonReturn: install(release)
        case .alertSecondButtonReturn: NSWorkspace.shared.open(release.page)
        default: break
        }
    }

    // MARK: Installing

    private func install(_ release: Release) {
        let app = Bundle.main.bundleURL
        let folder = app.deletingLastPathComponent()
        // An app macOS runs from a read-only copy (App Translocation), or one in a folder we
        // can't write to, can't replace itself.
        guard !app.path.contains("/AppTranslocation/"), FileManager.default.isWritableFile(atPath: folder.path) else {
            let alert = NSAlert()
            alert.messageText = "Onkey can't update himself from here"
            alert.informativeText = "Drag Onkey.app into your Applications folder, open him from there, and try again. Or download the new version yourself."
            alert.addButton(withTitle: "Open Download Page")
            alert.addButton(withTitle: "Cancel")
            NSApp.activate(ignoringOtherApps: true)
            if alert.runModal() == .alertFirstButtonReturn { NSWorkspace.shared.open(release.page) }
            return
        }
        downloading = true
        onChange?()
        URLSession.shared.downloadTask(with: release.download) { [weak self] file, _, error in
            let outcome: Result<URL, Error>
            if let file {
                outcome = Result { try Self.unpack(file, release: release, beside: app) }
            } else {
                outcome = .failure(error ?? UpdateError("The download didn't finish."))
            }
            DispatchQueue.main.async {
                guard let self else { return }
                self.downloading = false
                self.onChange?()
                switch outcome {
                case .success(let newApp): self.relaunch(replacing: app, with: newApp)
                case .failure(let error): self.say("Onkey couldn't update", error.localizedDescription)
                }
            }
        }.resume()
    }

    // Checks the download and unzips it into a scratch folder on the same disk as the app,
    // returning the new Onkey.app. Runs off the main thread.
    private static func unpack(_ zip: URL, release: Release, beside app: URL) throws -> URL {
        if let expected = release.sha256 {
            let digest = SHA256.hash(data: try Data(contentsOf: zip)).map { String(format: "%02x", $0) }.joined()
            guard digest == expected.lowercased() else {
                throw UpdateError("The download was damaged (its checksum didn't match). Try again later.")
            }
        }
        let scratch = try FileManager.default.url(for: .itemReplacementDirectory, in: .userDomainMask,
                                                  appropriateFor: app, create: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, scratch.path])
        let newApp = scratch.appendingPathComponent("Onkey.app")
        guard FileManager.default.fileExists(atPath: newApp.appendingPathComponent("Contents/MacOS/Onkey").path) else {
            throw UpdateError("The download didn't contain Onkey.app.")
        }
        // Downloads made by Onkey aren't quarantined, but make sure macOS won't block the new copy.
        try? run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", newApp.path])
        return newApp
    }

    // Quits, and once Onkey has fully exited swaps the new app into place and opens it. If
    // the swap fails the old app is put back and reopened instead.
    private func relaunch(replacing app: URL, with newApp: URL) {
        let script = """
            while kill -0 "$1" 2>/dev/null; do sleep 0.2; done
            rm -rf "$3.old"
            if mv "$3" "$3.old" && mv "$2" "$3"; then rm -rf "$3.old"; else [ -d "$3" ] || mv "$3.old" "$3"; fi
            rm -rf "$(dirname "$2")"
            open "$3"
            """
        let swap = Process()
        swap.executableURL = URL(fileURLWithPath: "/bin/sh")
        swap.arguments = ["-c", script, "onkey-update", String(getpid()), newApp.path, app.path]
        do {
            try swap.run()
        } catch {
            say("Onkey couldn't update", error.localizedDescription)
            return
        }
        NSApp.terminate(nil)
    }

    // MARK: Helpers

    private static func parse(_ data: Data) -> Release? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = json["tag_name"] as? String,
              let page = (json["html_url"] as? String).flatMap({ URL(string: $0) }),
              let assets = json["assets"] as? [[String: Any]],
              let asset = assets.first(where: { $0["name"] as? String == assetName }),
              let download = (asset["browser_download_url"] as? String).flatMap({ URL(string: $0) }) else { return nil }
        let digest = (asset["digest"] as? String).flatMap { $0.hasPrefix("sha256:") ? String($0.dropFirst(7)) : nil }
        let version = tag.hasPrefix("v") || tag.hasPrefix("V") ? String(tag.dropFirst()) : tag
        return Release(version: version, download: download, sha256: digest, page: page)
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

    private func say(_ title: String, _ detail: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = detail
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }
}

struct UpdateError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}
