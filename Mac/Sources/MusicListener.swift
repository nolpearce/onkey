import AVFoundation
import CoreAudio

// Listens to what the Mac is playing and feeds it to a BeatTracker. On macOS 14.2 and
// later that's a Core Audio tap on the system output (leaving out Onkey's own sound);
// older systems fall back to the microphone. Either asks for permission the first time.
final class MusicListener {
    private(set) var tracker: BeatTracker?
    private var tap: AnyObject?          // SystemAudioTap, which needs macOS 14.2.
    private var engine: AVAudioEngine?

    var current: BeatTracker.Beat { tracker?.current ?? BeatTracker.Beat() }

    func start() throws {
        stop()
        if #available(macOS 14.2, *) {
            let recorder = Recorder.fromEnvironment()
            let t = try SystemAudioTap { [weak self] samples, time in
                self?.tracker?.process(samples, endingAt: time)
                recorder?.add(samples)
            }
            tracker = BeatTracker(sampleRate: t.sampleRate)
            recorder?.sampleRate = t.sampleRate
            try t.start()
            tap = t
        } else {
            let e = AVAudioEngine()
            let input = e.inputNode
            let format = input.outputFormat(forBus: 0)
            let mono = Mono()
            tracker = BeatTracker(sampleRate: format.sampleRate)
            input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
                mono.feed(buffer) { self?.tracker?.process($0, endingAt: ProcessInfo.processInfo.systemUptime) }
            }
            try e.start()
            engine = e
        }
    }

    func stop() {
        if #available(macOS 14.2, *) { (tap as? SystemAudioTap)?.stop() }
        tap = nil
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
        engine = nil
        tracker = nil
    }
}

// For tuning the beat tracker: with ONKEY_RECORD=/path/file.wav set, saves the first minute
// of what Onkey hears (mono) to that file. Off unless that variable is set.
private final class Recorder {
    var sampleRate = 48000.0
    private let url: URL
    private var samples: [Float] = []
    private var saved = false

    static func fromEnvironment() -> Recorder? {
        guard let path = ProcessInfo.processInfo.environment["ONKEY_RECORD"] else { return nil }
        return Recorder(url: URL(fileURLWithPath: path))
    }
    private init(url: URL) { self.url = url }

    func add(_ chunk: UnsafeBufferPointer<Float>) {
        guard !saved else { return }
        samples.append(contentsOf: chunk)
        guard Double(samples.count) >= sampleRate * 60 else { return }
        saved = true
        guard let format = AVAudioFormat(standardFormatWithSampleRate: sampleRate, channels: 1),
              let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(samples.count)),
              let file = try? AVAudioFile(forWriting: url, settings: format.settings) else { return }
        buffer.frameLength = buffer.frameCapacity
        samples.withUnsafeBufferPointer { buffer.floatChannelData![0].update(from: $0.baseAddress!, count: $0.count) }
        try? file.write(from: buffer)
        NSLog("Onkey: saved a minute of audio to %@", url.path)
    }
}

// Mixes a buffer's channels down to one, reusing its scratch space between calls.
private final class Mono {
    private var scratch = [Float]()

    func feed(_ buffer: AVAudioPCMBuffer, _ body: (UnsafeBufferPointer<Float>) -> Void) {
        guard let data = buffer.floatChannelData else { return }
        let frames = Int(buffer.frameLength), channels = Int(buffer.format.channelCount), stride = buffer.stride
        if scratch.count < frames { scratch = [Float](repeating: 0, count: frames) }
        let scale = 1 / Float(max(1, channels))
        scratch.withUnsafeMutableBufferPointer { out in
            for f in 0..<frames {
                var sum: Float = 0
                for c in 0..<channels {
                    // Interleaved audio has every channel in data[0]; otherwise one per channel.
                    sum += buffer.format.isInterleaved ? data[0][f * stride + c] : data[c][f]
                }
                out[f] = sum * scale
            }
            body(UnsafeBufferPointer(start: out.baseAddress, count: frames))
        }
    }
}

struct AudioError: LocalizedError {
    let what: String, status: OSStatus
    var errorDescription: String? { "\(what) failed (error \(status))." }
}

private func check(_ status: OSStatus, _ what: String) throws {
    if status != noErr { throw AudioError(what: what, status: status) }
}

// A private, unmuted tap on everything the Mac plays, wrapped in a private aggregate
// device so it can be read like an input.
@available(macOS 14.2, *)
private final class SystemAudioTap {
    let sampleRate: Double
    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var deviceID = AudioObjectID(kAudioObjectUnknown)
    private var procID: AudioDeviceIOProcID?
    private let format: AVAudioFormat
    private let onSamples: (UnsafeBufferPointer<Float>, Double) -> Void
    private let mono = Mono()
    private let queue = DispatchQueue(label: "Onkey.MusicListener")

    init(onSamples: @escaping (UnsafeBufferPointer<Float>, Double) -> Void) throws {
        self.onSamples = onSamples
        let description = CATapDescription(stereoGlobalTapButExcludeProcesses: Self.ownAudioProcess())
        description.uuid = UUID()
        description.isPrivate = true
        description.muteBehavior = .unmuted
        try check(AudioHardwareCreateProcessTap(description, &tapID), "Creating the audio tap")

        var asbd = AudioStreamBasicDescription()
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        var address = AudioObjectPropertyAddress(mSelector: kAudioTapPropertyFormat, mScope: kAudioObjectPropertyScopeGlobal,
                                                 mElement: kAudioObjectPropertyElementMain)
        try check(AudioObjectGetPropertyData(tapID, &address, 0, nil, &size, &asbd), "Reading the tap's format")
        guard let f = AVAudioFormat(streamDescription: &asbd) else { throw AudioError(what: "Reading the tap's format", status: -1) }
        format = f
        sampleRate = asbd.mSampleRate

        let outputUID = try Self.defaultOutputUID()
        let aggregate: [String: Any] = [
            kAudioAggregateDeviceNameKey: "Onkey Listener",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceMainSubDeviceKey: outputUID,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outputUID]],
            kAudioAggregateDeviceTapListKey: [[kAudioSubTapDriftCompensationKey: true,
                                               kAudioSubTapUIDKey: description.uuid.uuidString]],
        ]
        try check(AudioHardwareCreateAggregateDevice(aggregate as CFDictionary, &deviceID), "Creating the listening device")
    }

    func start() throws {
        let format = self.format, mono = self.mono, onSamples = self.onSamples
        try check(AudioDeviceCreateIOProcIDWithBlock(&procID, deviceID, queue) { _, input, _, _, _ in
            guard let buffer = AVAudioPCMBuffer(pcmFormat: format, bufferListNoCopy: input, deallocator: nil) else { return }
            mono.feed(buffer) { onSamples($0, ProcessInfo.processInfo.systemUptime) }
        }, "Listening to the audio tap")
        try check(AudioDeviceStart(deviceID, procID), "Starting to listen")
    }

    func stop() {
        if let procID {
            AudioDeviceStop(deviceID, procID)
            AudioDeviceDestroyIOProcID(deviceID, procID)
            self.procID = nil
        }
        if deviceID != kAudioObjectUnknown { AudioHardwareDestroyAggregateDevice(deviceID); deviceID = kAudioObjectUnknown }
        if tapID != kAudioObjectUnknown { AudioHardwareDestroyProcessTap(tapID); tapID = kAudioObjectUnknown }
    }

    deinit { stop() }

    // Onkey's own audio object, so his "oooo" isn't mistaken for a beat. Empty if he
    // hasn't made a sound yet (macOS only creates it once a process plays audio).
    private static func ownAudioProcess() -> [AudioObjectID] {
        var pid = getpid()
        var object = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyTranslatePIDToProcessObject,
                                                 mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        let status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address,
                                                UInt32(MemoryLayout<pid_t>.size), &pid, &size, &object)
        return status == noErr && object != kAudioObjectUnknown ? [object] : []
    }

    private static func defaultOutputUID() throws -> String {
        var device = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultSystemOutputDevice,
                                                 mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        try check(AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &device),
                  "Finding the speakers")
        var uid: Unmanaged<CFString>?
        size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        address.mSelector = kAudioDevicePropertyDeviceUID
        try check(AudioObjectGetPropertyData(device, &address, 0, nil, &size, &uid), "Finding the speakers' ID")
        guard let uid else { throw AudioError(what: "Finding the speakers' ID", status: -1) }
        return uid.takeRetainedValue() as String
    }
}
