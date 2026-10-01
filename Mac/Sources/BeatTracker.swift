import Foundation

// Finds the beat in whatever audio it's fed. Every 10 ms it measures how much the sound
// suddenly got louder (spectral flux) in four frequency bands: kick drums, bass and low
// percussion, snares/bells/guitar, and hi-hats/shakers. Each band's hits are checked for how
// regularly they repeat over a bar, and the steadiest bands get the most say in the tempo, so
// a song driven by a cowbell is followed as well as one driven by a kick. Where the beat lands
// also leans on bass thumps, since kick drums mark the beat while hi-hats often sit on the
// off-beat. Mirrors BeatTracker.cs on Windows.
final class BeatTracker {
    struct Beat {
        var active = false        // Music with a clear beat is playing.
        var lastBeat = 0.0        // When a beat landed, in the same clock as `process`.
        var period = 0.5          // Seconds between beats.
        var confidence = 0.0      // How strongly the onsets repeat at that tempo, 0...1.
    }

    private static let hopsPerSecond = 100.0
    private static let historyHops = 800          // 8 seconds of onsets.
    private static let minLag = 30, maxLag = 100  // 200 down to 60 beats per minute.
    private static let multiples = 4              // A tempo is checked over this many beats (a bar).
    // How loud the music has been lately is remembered as a peak that slowly falls away.
    private static let levelFall = 0.9985         // Per hop: halves in about 4.6 seconds.
    private static let bandEdges = [0.0, 150, 600, 3000]   // Hz where each band starts.
    private static let blur = [1, 0.7, 0.25]               // Onset smoothing, by hops away.

    private let sampleRate: Double
    private let hop: Int
    // The last ~23 ms of audio, and the spectrum of the previous slice to compare against.
    private let fft: FFT
    private var ring: [Double], ringWrite = 0
    private var previous: [Double]
    private var bandOf: [Int]                     // Which band each FFT bin belongs to.
    private var bandOnsets: [[Double]]
    private var fullSum = 0.0, level = 1e-6, count = 0
    // Low-pass filter (around 150 Hz) for the bass thumps.
    private let b0, b1, b2, a1, a2: Double
    private var x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0
    private var bassSum = 0.0, lastBassLog = 0.0, bassLevel = 1e-10
    private var bassOnsets = [Double](repeating: 0, count: historyHops)
    private var loudness = [Double](repeating: 0, count: historyHops)
    private var write = 0, hopsSeen = 0
    private var periodHops = 50.0
    // A different tempo has to win a few analyses in a row before it's believed, and the
    // beat has to be clear (or unclear) twice running to start (or stop) the dancing.
    private var candidateHops = 0.0, candidateWins = 0
    private var clearRuns = 0, unclearRuns = 0, active = false
    private var phaseMisses = 0
    private var beat = Beat()
    private let lock = NSLock()

    init(sampleRate: Double) {
        self.sampleRate = sampleRate
        hop = max(1, Int(sampleRate / Self.hopsPerSecond))
        var size = 256
        while Double(size) < sampleRate * 0.023 { size *= 2 }
        fft = FFT(size: size)
        ring = [Double](repeating: 0, count: size)
        previous = [Double](repeating: 0, count: size / 2)
        bandOf = (0..<size / 2).map { k in
            let hz = Double(k) * sampleRate / Double(size)
            return Self.bandEdges.lastIndex { hz >= $0 } ?? 0
        }
        bandOnsets = Array(repeating: [Double](repeating: 0, count: Self.historyHops), count: Self.bandEdges.count)
        let w0 = 2 * Double.pi * 150 / sampleRate, alpha = sin(w0) / (2 * 0.7071)
        let a0 = 1 + alpha
        b0 = (1 - cos(w0)) / 2 / a0; b1 = (1 - cos(w0)) / a0; b2 = b0
        a1 = -2 * cos(w0) / a0; a2 = (1 - alpha) / a0
    }

    // The latest beat estimate. Safe to call from any thread.
    var current: Beat {
        lock.lock(); defer { lock.unlock() }
        return beat
    }

    // Mono samples; `time` is when the last of them was heard.
    func process(_ samples: UnsafeBufferPointer<Float>, endingAt time: Double) {
        let n = samples.count
        for i in 0..<n {
            let x = Double(samples[i])
            ring[ringWrite] = x
            ringWrite = (ringWrite + 1) % ring.count
            let y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            x2 = x1; x1 = x; y2 = y1; y1 = y
            bassSum += y * y; fullSum += x * x; count += 1
            if count == hop { finishHop(at: time - Double(n - 1 - i) / sampleRate) }
        }
    }

    private func finishHop(at time: Double) {
        // Log-compressed spectrum of the latest slice, measured against how loud the music has
        // been lately so every song is compressed alike however loud it is (against full scale,
        // held chords that slowly wobble were often taken for a beat); the flux is how much it
        // rose since the last.
        let magnitudes = fft.magnitudes(of: ring, startingAt: ringWrite)
        var average = 0.0
        for k in 1..<magnitudes.count { average += magnitudes[k] }
        average /= Double(magnitudes.count - 1)
        level = max(1e-6, average, level * Self.levelFall)
        let gain = 1 / level
        var flux = [Double](repeating: 0, count: Self.bandEdges.count)
        for k in 1..<magnitudes.count {
            let m = log(1 + gain * magnitudes[k])
            flux[bandOf[k]] += max(0, m - previous[k])
            previous[k] = m
        }
        for b in 0..<flux.count { bandOnsets[b][write] = flux[b] }
        // Bass thumps, also against how loud the bass has been lately, so a soft pickup note
        // after a gap doesn't count as much as the downbeat.
        let bassPower = bassSum / Double(hop)
        bassLevel = max(1e-10, bassPower, bassLevel * Self.levelFall)
        let bassLog = log(1 + 30 * bassPower / bassLevel)
        bassOnsets[write] = max(0, bassLog - lastBassLog)
        lastBassLog = bassLog
        loudness[write] = fullSum / Double(hop)
        write = (write + 1) % Self.historyHops
        fullSum = 0; bassSum = 0; count = 0
        hopsSeen += 1
        if hopsSeen % 25 == 0 { analyse(now: time) }
    }

    // Onsets oldest first, keeping only what stands above the local average (so a loud
    // stretch doesn't count as one long hit), blurred over a few hops (so a drummer a little
    // early or late still lines up with the beat before), then with the overall average removed.
    private func history(_ source: [Double]) -> [Double] {
        let n = min(hopsSeen, Self.historyHops)
        var raw = [Double](repeating: 0, count: n)
        for i in 0..<n { raw[i] = source[(write - n + i + Self.historyHops) % Self.historyHops] }
        var peaks = [Double](repeating: 0, count: n)
        var windowSum = 0.0
        let half = 12
        for i in 0..<min(n, half) { windowSum += raw[i] }
        for i in 0..<n {
            if i + half < n { windowSum += raw[i + half] }
            if i - half - 1 >= 0 { windowSum -= raw[i - half - 1] }
            let width = min(n - 1, i + half) - max(0, i - half) + 1
            peaks[i] = max(0, raw[i] - windowSum / Double(width))
        }
        var out = [Double](repeating: 0, count: n)
        for i in 0..<n {
            var s = 0.0, w = 0.0
            for j in (1 - Self.blur.count)..<Self.blur.count where i + j >= 0 && i + j < n {
                let k = Self.blur[abs(j)]
                s += peaks[i + j] * k; w += k
            }
            out[i] = s / w
        }
        let mean = out.reduce(0, +) / Double(max(1, n))
        return out.map { $0 - mean }
    }

    // How much a lag (in hops) is favoured as the beat: most around 120 beats per minute, so a
    // beat isn't mistaken for half or double itself.
    private static func prior(_ lag: Int) -> Double {
        let octaves = log2(Double(lag) / 50)
        return exp(-0.5 * octaves * octaves / 0.8)
    }

    private func analyse(now: Double) {
        guard hopsSeen >= 300 else { return }   // Needs 3 seconds to judge a tempo.
        var recentLoudness = 0.0
        for i in 1...100 { recentLoudness += loudness[(write - i + Self.historyHops) % Self.historyHops] }
        let rms = (recentLoudness / 100).squareRoot()

        // Each band's self-similarity half a beat and one, two, three and four beats later,
        // averaged. Checking a whole bar matters for syncopated grooves (clave, dembow, tumbao),
        // where little repeats exactly one beat later but everything repeats a bar later;
        // checking only one beat let a part that repeats every dotted beat win, giving two-thirds
        // or four-thirds of the tempo. The half beat favours the real beat over a dotted one,
        // since nearly every groove has eighth notes. A band's say in the tempo grows with how
        // regular it is (the square of its best match), so steady instruments lead.
        let bands = bandOnsets.map { history($0) }
        let n = bands[0].count
        let longest = min(Self.multiples * (Self.maxLag + 1), n / 2)
        var scores = [Double](repeating: 0, count: Self.maxLag + 2)
        var weights = [Double](repeating: 0, count: bands.count)
        var similarity = [Double](repeating: 0, count: longest + 1)
        for (b, o) in bands.enumerated() {
            var r0 = 0.0
            for v in o { r0 += v * v }
            guard r0 > 0 else { continue }
            r0 /= Double(n)
            // Averaged per overlapping pair, so long lags (slow songs) aren't marked down for
            // overlapping less.
            for lag in ((Self.minLag - 1) / 2)...longest {
                var r = 0.0
                for i in lag..<n { r += o[i] * o[i - lag] }
                similarity[lag] = r / Double(n - lag) / r0
            }
            var own = [Double](repeating: 0, count: Self.maxLag + 2)
            var peak = 0.0
            for lag in (Self.minLag - 1)...(Self.maxLag + 1) {
                var sum = similarity[(lag + 1) / 2], used = 1
                var k = 1
                while k <= Self.multiples && k * lag <= longest { sum += similarity[k * lag]; used += 1; k += 1 }
                own[lag] = sum / Double(used)
                if lag >= Self.minLag && lag <= Self.maxLag { peak = max(peak, own[lag]) }
            }
            weights[b] = peak * peak
            for lag in 0..<own.count { scores[lag] += weights[b] * own[lag] }
        }
        let totalWeight = weights.reduce(0, +)
        guard totalWeight > 0 else { active = false; publish(Beat(active: false, lastBeat: beat.lastBeat, period: beat.period)); return }
        for lag in 0..<scores.count { scores[lag] /= totalWeight }
        // Tempo: the lag with the strongest self-similarity, nudged toward ~120 BPM.
        var bestLag = Self.minLag, bestScore = -Double.infinity
        for lag in Self.minLag...Self.maxLag {
            let weighted = scores[lag] * Self.prior(lag)
            if weighted > bestScore { bestScore = weighted; bestLag = lag }
        }
        // Confidence: how closely the onsets match themselves over the next bar. Test grooves
        // (pop, house, salsa, bossa nova, reggaeton, cumbia, quiet ballads) measured 0.35-0.65,
        // and noise or notes played at random under 0.1. It takes 0.38 to start dancing but only
        // dropping under 0.2 to stop, so quieter passages don't interrupt a song.
        let confidence = scores[bestLag]
        // Parabolic fit between neighbouring lags for a fractional period.
        let l = scores[bestLag - 1], c = scores[bestLag], r = scores[bestLag + 1]
        let denom = l - 2 * c + r
        let refined = Double(bestLag) + (denom < 0 ? 0.5 * (l - r) / denom : 0)
        if abs(refined - periodHops) / periodHops < 0.1 {
            periodHops = 0.7 * periodHops + 0.3 * refined
            candidateWins = 0
        } else if candidateWins > 0 && abs(refined - candidateHops) / candidateHops < 0.1 {
            candidateWins += 1
            candidateHops = refined
            if candidateWins >= 3 || !active { periodHops = refined; candidateWins = 0 }
        } else {
            candidateHops = refined; candidateWins = 1
            if !active { periodHops = refined }
        }

        // Phase: the offset where onsets one, two, three... beats back are strongest. The bands
        // count by the same weights as for the tempo, plus the bass thumps as an anchor for
        // the downbeat (each curve scaled to the same size first).
        let bass = history(bassOnsets)
        func spread(_ v: [Double]) -> Double { max(1e-9, (v.reduce(0) { $0 + $1 * $1 } / Double(max(1, v.count))).squareRoot()) }
        var combined = [Double](repeating: 0, count: n)
        for (b, o) in bands.enumerated() where weights[b] > 0 {
            let scale = weights[b] / totalWeight / spread(o)
            for i in 0..<n { combined[i] += o[i] * scale }
        }
        let bassScale = 1 / spread(bass)
        for i in 0..<n { combined[i] += bass[i] * bassScale }
        let span = Int(periodHops.rounded())
        var bestOffset = 0, bestPhase = -Double.infinity
        for offset in 0..<span {
            var s = 0.0
            var k = 0.0
            while true {
                let i = n - 1 - offset - Int((k * periodHops).rounded())
                if i < 0 { break }
                s += combined[i]
                k += 1
            }
            if s > bestPhase { bestPhase = s; bestOffset = offset }
        }
        let clear = rms > 0.002 && confidence >= 0.38, unclear = rms <= 0.002 || confidence < 0.2
        clearRuns = clear ? clearRuns + 1 : 0
        unclearRuns = unclear ? unclearRuns + 1 : 0
        let wasActive = active
        if clearRuns >= 2 { active = true }
        if unclearRuns >= 2 { active = false }

        // Keep the beat steady: small timing corrections are eased in, and a jump to a
        // different spot (usually the off-beat) has to hold for five analyses (2.5 s) first.
        let period = periodHops / Self.hopsPerSecond
        let heard = now - Double(bestOffset) / Self.hopsPerSecond
        var lastBeat = heard
        if wasActive {
            let predicted = beat.lastBeat + ((heard - beat.lastBeat) / period).rounded() * period
            let drift = heard - predicted
            if abs(drift) < 0.2 * period {
                lastBeat = predicted + drift * 0.5
                phaseMisses = 0
            } else {
                phaseMisses += 1
                lastBeat = phaseMisses >= 5 ? heard : predicted
                if phaseMisses >= 5 { phaseMisses = 0 }
            }
        }
        publish(Beat(active: active, lastBeat: lastBeat, period: period, confidence: confidence))
    }

    private func publish(_ b: Beat) {
        lock.lock(); beat = b; lock.unlock()
    }
}

// A small radix-2 FFT (no dependencies, so the Windows port can match it exactly).
final class FFT {
    let size: Int
    private let window: [Double]
    private let cosines: [Double], sines: [Double]
    private let reversed: [Int]
    private var re: [Double], im: [Double]

    init(size: Int) {
        self.size = size
        window = (0..<size).map { 0.5 - 0.5 * cos(2 * Double.pi * Double($0) / Double(size)) }   // Hann.
        cosines = (0..<size / 2).map { cos(-2 * Double.pi * Double($0) / Double(size)) }
        sines = (0..<size / 2).map { sin(-2 * Double.pi * Double($0) / Double(size)) }
        let bits = Int(log2(Double(size)))
        reversed = (0..<size).map { i in
            var r = 0, v = i
            for _ in 0..<bits { r = (r << 1) | (v & 1); v >>= 1 }
            return r
        }
        re = [Double](repeating: 0, count: size)
        im = [Double](repeating: 0, count: size)
    }

    // Magnitudes of the first half of the spectrum of `ring`, read oldest first from `start`.
    func magnitudes(of ring: [Double], startingAt start: Int) -> [Double] {
        for i in 0..<size {
            re[reversed[i]] = ring[(start + i) % size] * window[i]
            im[reversed[i]] = 0
        }
        var length = 2
        while length <= size {
            let half = length / 2, step = size / length
            var block = 0
            while block < size {
                for j in 0..<half {
                    let wr = cosines[j * step], wi = sines[j * step]
                    let a = block + j, b = a + half
                    let tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr
                    re[b] = re[a] - tr; im[b] = im[a] - ti
                    re[a] += tr; im[a] += ti
                }
                block += length
            }
            length *= 2
        }
        var out = [Double](repeating: 0, count: size / 2)
        for k in 0..<size / 2 { out[k] = (re[k] * re[k] + im[k] * im[k]).squareRoot() / Double(size) }
        return out
    }
}
