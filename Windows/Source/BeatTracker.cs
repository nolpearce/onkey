using System;
using System.Collections.Generic;

namespace OnkeyDesktopPet
{
    // Finds the beat in whatever audio it's fed. Every 10 ms it measures how much the sound
    // suddenly got louder (spectral flux) in four frequency bands: kick drums, bass and low
    // percussion, snares/bells/guitar, and hi-hats/shakers. Each band's hits are checked for how
    // regularly they repeat over a bar, and the steadiest bands get the most say in the tempo, so
    // a song driven by a cowbell is followed as well as one driven by a kick. Where the beat lands
    // also leans on bass thumps, since kick drums mark the beat while hi-hats often sit on the
    // off-beat. Mirrors BeatTracker.swift on Mac.
    public sealed class BeatTracker
    {
        public struct Beat
        {
            public bool Active;          // Music with a clear beat is playing.
            public double LastBeat;      // When a beat landed, in the same clock as Process.
            public double Period;        // Seconds between beats.
            public double Confidence;    // How strongly the onsets repeat at that tempo, 0...1.
        }

        private const double HopsPerSecond = 100;
        private const int HistoryHops = 800;          // 8 seconds of onsets.
        private const int MinLag = 30, MaxLag = 100;  // 200 down to 60 beats per minute.
        private const int Multiples = 4;              // A tempo is checked over this many beats (a bar).
        // How loud the music has been lately is remembered as a peak that slowly falls away.
        private const double LevelFall = 0.9985;      // Per hop: halves in about 4.6 seconds.
        private static readonly double[] BandEdges = { 0, 150, 600, 3000 };   // Hz where each band starts.
        private static readonly double[] Blur = { 1, 0.7, 0.25 };              // Onset smoothing, by hops away.

        private readonly double sampleRate;
        private readonly int hop;
        // The last ~23 ms of audio, and the spectrum of the previous slice to compare against.
        private readonly Fft fft;
        private readonly double[] ring;
        private int ringWrite;
        private readonly double[] previous;
        private readonly int[] bandOf;                 // Which band each FFT bin belongs to.
        private readonly double[][] bandOnsets;
        private double fullSum, level = 1e-6;
        private int count;
        // Low-pass filter (around 150 Hz) for the bass thumps.
        private readonly double b0, b1, b2, a1, a2;
        private double x1, x2, y1, y2;
        private double bassSum, lastBassLog, bassLevel = 1e-10;
        private readonly double[] bassOnsets = new double[HistoryHops];
        private readonly double[] loudness = new double[HistoryHops];
        private int write, hopsSeen;
        private double periodHops = 50;
        // A different tempo has to win a few analyses in a row before it's believed, and the
        // beat has to be clear (or unclear) twice running to start (or stop) the dancing.
        private double candidateHops;
        private int candidateWins, clearRuns, unclearRuns, phaseMisses;
        private bool active;
        private Beat beat;
        private readonly object gate = new object();

        public BeatTracker(double sampleRate)
        {
            this.sampleRate = sampleRate;
            hop = Math.Max(1, (int)(sampleRate / HopsPerSecond));
            int size = 256;
            while (size < sampleRate * 0.023) size *= 2;
            fft = new Fft(size);
            ring = new double[size];
            previous = new double[size / 2];
            bandOf = new int[size / 2];
            for (int k = 0; k < bandOf.Length; k++)
            {
                double hz = k * sampleRate / size;
                for (int b = 0; b < BandEdges.Length; b++) if (hz >= BandEdges[b]) bandOf[k] = b;
            }
            bandOnsets = new double[BandEdges.Length][];
            for (int b = 0; b < bandOnsets.Length; b++) bandOnsets[b] = new double[HistoryHops];
            double w0 = 2 * Math.PI * 150 / sampleRate, alpha = Math.Sin(w0) / (2 * 0.7071);
            double a0 = 1 + alpha;
            b0 = (1 - Math.Cos(w0)) / 2 / a0; b1 = (1 - Math.Cos(w0)) / a0; b2 = b0;
            a1 = -2 * Math.Cos(w0) / a0; a2 = (1 - alpha) / a0;
            beat.Period = 0.5;
        }

        // The latest beat estimate. Safe to call from any thread.
        public Beat Current { get { lock (gate) return beat; } }

        // Mono samples; time is when the last of them was heard.
        public void Process(float[] samples, int length, double time)
        {
            for (int i = 0; i < length; i++)
            {
                double x = samples[i];
                ring[ringWrite] = x;
                ringWrite = (ringWrite + 1) % ring.Length;
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                bassSum += y * y; fullSum += x * x; count++;
                if (count == hop) FinishHop(time - (length - 1 - i) / sampleRate);
            }
        }

        private void FinishHop(double time)
        {
            // Log-compressed spectrum of the latest slice, measured against how loud the music
            // has been lately so every song is compressed alike however loud it is (against full
            // scale, held chords that slowly wobble were often taken for a beat); the flux is how
            // much it rose since the last.
            double[] magnitudes = fft.Magnitudes(ring, ringWrite);
            double average = 0;
            for (int k = 1; k < magnitudes.Length; k++) average += magnitudes[k];
            average /= magnitudes.Length - 1;
            level = Math.Max(1e-6, Math.Max(average, level * LevelFall));
            double gain = 1 / level;
            double[] flux = new double[BandEdges.Length];
            for (int k = 1; k < magnitudes.Length; k++)
            {
                double m = Math.Log(1 + gain * magnitudes[k]);
                flux[bandOf[k]] += Math.Max(0, m - previous[k]);
                previous[k] = m;
            }
            for (int b = 0; b < flux.Length; b++) bandOnsets[b][write] = flux[b];
            // Bass thumps, also against how loud the bass has been lately, so a soft pickup note
            // after a gap doesn't count as much as the downbeat.
            double bassPower = bassSum / hop;
            bassLevel = Math.Max(1e-10, Math.Max(bassPower, bassLevel * LevelFall));
            double bassLog = Math.Log(1 + 30 * bassPower / bassLevel);
            bassOnsets[write] = Math.Max(0, bassLog - lastBassLog);
            lastBassLog = bassLog;
            loudness[write] = fullSum / hop;
            write = (write + 1) % HistoryHops;
            fullSum = 0; bassSum = 0; count = 0;
            hopsSeen++;
            if (hopsSeen % 25 == 0) Analyse(time);
        }

        // Onsets oldest first, keeping only what stands above the local average (so a loud
        // stretch doesn't count as one long hit), blurred over a few hops (so a drummer a
        // little early or late still lines up with the beat before), then with the overall
        // average removed.
        private double[] History(double[] source)
        {
            int n = Math.Min(hopsSeen, HistoryHops);
            double[] raw = new double[n];
            for (int i = 0; i < n; i++) raw[i] = source[(write - n + i + HistoryHops) % HistoryHops];
            double[] peaks = new double[n];
            double windowSum = 0;
            const int half = 12;
            for (int i = 0; i < Math.Min(n, half); i++) windowSum += raw[i];
            for (int i = 0; i < n; i++)
            {
                if (i + half < n) windowSum += raw[i + half];
                if (i - half - 1 >= 0) windowSum -= raw[i - half - 1];
                int width = Math.Min(n - 1, i + half) - Math.Max(0, i - half) + 1;
                peaks[i] = Math.Max(0, raw[i] - windowSum / width);
            }
            double[] o = new double[n];
            double mean = 0;
            for (int i = 0; i < n; i++)
            {
                double s = 0, w = 0;
                for (int j = -Blur.Length + 1; j < Blur.Length; j++)
                {
                    if (i + j < 0 || i + j >= n) continue;
                    double k = Blur[Math.Abs(j)];
                    s += peaks[i + j] * k; w += k;
                }
                o[i] = s / w;
                mean += o[i];
            }
            mean /= Math.Max(1, n);
            for (int i = 0; i < n; i++) o[i] -= mean;
            return o;
        }

        private static double Spread(double[] v)
        {
            double sum = 0;
            foreach (double x in v) sum += x * x;
            return Math.Max(1e-9, Math.Sqrt(sum / Math.Max(1, v.Length)));
        }

        // How much a lag (in hops) is favoured as the beat: most around 120 beats per minute, so a
        // beat isn't mistaken for half or double itself.
        private static double Prior(int lag)
        {
            double octaves = Math.Log(lag / 50.0, 2);
            return Math.Exp(-0.5 * octaves * octaves / 0.8);
        }

        private void Analyse(double now)
        {
            if (hopsSeen < 300) return;   // Needs 3 seconds to judge a tempo.
            double recentLoudness = 0;
            for (int i = 1; i <= 100; i++) recentLoudness += loudness[(write - i + HistoryHops) % HistoryHops];
            double rms = Math.Sqrt(recentLoudness / 100);

            // Each band's self-similarity half a beat and one, two, three and four beats later,
            // averaged. Checking a whole bar matters for syncopated grooves (clave, dembow,
            // tumbao), where little repeats exactly one beat later but everything repeats a bar
            // later; checking only one beat let a part that repeats every dotted beat win, giving
            // two-thirds or four-thirds of the tempo. The half beat favours the real beat over a
            // dotted one, since nearly every groove has eighth notes. A band's say in the tempo
            // grows with how regular it is (the square of its best match), so steady instruments
            // lead.
            double[][] bands = new double[bandOnsets.Length][];
            for (int b = 0; b < bands.Length; b++) bands[b] = History(bandOnsets[b]);
            int n = bands[0].Length;
            int longest = Math.Min(Multiples * (MaxLag + 1), n / 2);
            double[] scores = new double[MaxLag + 2];
            double[] weights = new double[bands.Length];
            double[] similarity = new double[longest + 1];
            for (int b = 0; b < bands.Length; b++)
            {
                double[] o = bands[b];
                double r0 = 0;
                foreach (double v in o) r0 += v * v;
                if (r0 <= 0) continue;
                r0 /= n;
                // Averaged per overlapping pair, so long lags (slow songs) aren't marked down
                // for overlapping less.
                for (int lag = (MinLag - 1) / 2; lag <= longest; lag++)
                {
                    double r = 0;
                    for (int i = lag; i < n; i++) r += o[i] * o[i - lag];
                    similarity[lag] = r / (n - lag) / r0;
                }
                double[] own = new double[MaxLag + 2];
                double peak = 0;
                for (int lag = MinLag - 1; lag <= MaxLag + 1; lag++)
                {
                    double sum = similarity[(lag + 1) / 2];
                    int used = 1;
                    for (int k = 1; k <= Multiples && k * lag <= longest; k++) { sum += similarity[k * lag]; used++; }
                    own[lag] = sum / used;
                    if (lag >= MinLag && lag <= MaxLag) peak = Math.Max(peak, own[lag]);
                }
                weights[b] = peak * peak;
                for (int lag = 0; lag < own.Length; lag++) scores[lag] += weights[b] * own[lag];
            }
            double totalWeight = 0;
            foreach (double w in weights) totalWeight += w;
            if (totalWeight <= 0)
            {
                active = false;
                lock (gate) beat.Active = false;
                return;
            }
            for (int lag = 0; lag < scores.Length; lag++) scores[lag] /= totalWeight;
            // Tempo: the lag with the strongest self-similarity, nudged toward ~120 BPM.
            int bestLag = MinLag;
            double bestScore = double.NegativeInfinity;
            for (int lag = MinLag; lag <= MaxLag; lag++)
            {
                double weighted = scores[lag] * Prior(lag);
                if (weighted > bestScore) { bestScore = weighted; bestLag = lag; }
            }
            // Confidence: how closely the onsets match themselves over the next bar. Test grooves
            // (pop, house, salsa, bossa nova, reggaeton, cumbia, quiet ballads) measured 0.35-0.65,
            // and noise or notes played at random under 0.1. It takes 0.38 to start dancing but
            // only dropping under 0.2 to stop, so quieter passages don't interrupt a song.
            double confidence = scores[bestLag];
            // Parabolic fit between neighbouring lags for a fractional period.
            double l = scores[bestLag - 1], c = scores[bestLag], rr = scores[bestLag + 1];
            double denom = l - 2 * c + rr;
            double refined = bestLag + (denom < 0 ? 0.5 * (l - rr) / denom : 0);
            if (Math.Abs(refined - periodHops) / periodHops < 0.1)
            {
                periodHops = 0.7 * periodHops + 0.3 * refined;
                candidateWins = 0;
            }
            else if (candidateWins > 0 && Math.Abs(refined - candidateHops) / candidateHops < 0.1)
            {
                candidateWins++;
                candidateHops = refined;
                if (candidateWins >= 3 || !active) { periodHops = refined; candidateWins = 0; }
            }
            else
            {
                candidateHops = refined; candidateWins = 1;
                if (!active) periodHops = refined;
            }

            // Phase: the offset where onsets one, two, three... beats back are strongest. The bands
            // count by the same weights as for the tempo, plus the bass thumps as an anchor for
            // the downbeat (each curve scaled to the same size first).
            double[] bass = History(bassOnsets);
            double[] combined = new double[n];
            for (int b = 0; b < bands.Length; b++)
            {
                if (weights[b] <= 0) continue;
                double scale = weights[b] / totalWeight / Spread(bands[b]);
                for (int i = 0; i < n; i++) combined[i] += bands[b][i] * scale;
            }
            double bassScale = 1 / Spread(bass);
            for (int i = 0; i < n; i++) combined[i] += bass[i] * bassScale;
            int span = (int)Math.Round(periodHops);
            int bestOffset = 0;
            double bestPhase = double.NegativeInfinity;
            for (int offset = 0; offset < span; offset++)
            {
                double s = 0;
                for (int k = 0; ; k++)
                {
                    int i = n - 1 - offset - (int)Math.Round(k * periodHops);
                    if (i < 0) break;
                    s += combined[i];
                }
                if (s > bestPhase) { bestPhase = s; bestOffset = offset; }
            }
            bool clear = rms > 0.002 && confidence >= 0.38, unclear = rms <= 0.002 || confidence < 0.2;
            clearRuns = clear ? clearRuns + 1 : 0;
            unclearRuns = unclear ? unclearRuns + 1 : 0;
            bool wasActive = active;
            if (clearRuns >= 2) active = true;
            if (unclearRuns >= 2) active = false;

            // Keep the beat steady: small timing corrections are eased in, and a jump to a
            // different spot (usually the off-beat) has to hold for five analyses (2.5 s) first.
            double period = periodHops / HopsPerSecond;
            double heard = now - bestOffset / HopsPerSecond;
            double lastBeat = heard;
            if (wasActive)
            {
                double predicted = beat.LastBeat + Math.Round((heard - beat.LastBeat) / period) * period;
                double drift = heard - predicted;
                if (Math.Abs(drift) < 0.2 * period)
                {
                    lastBeat = predicted + drift * 0.5;
                    phaseMisses = 0;
                }
                else
                {
                    phaseMisses++;
                    lastBeat = phaseMisses >= 5 ? heard : predicted;
                    if (phaseMisses >= 5) phaseMisses = 0;
                }
            }
            Beat next = new Beat();
            next.Active = active;
            next.LastBeat = lastBeat;
            next.Period = period;
            next.Confidence = confidence;
            lock (gate) beat = next;
        }
    }

    // A small radix-2 FFT (no dependencies, and the same as the Mac version's).
    internal sealed class Fft
    {
        private readonly int size;
        private readonly double[] window, cosines, sines, re, im, magnitudes;
        private readonly int[] reversed;

        public Fft(int size)
        {
            this.size = size;
            window = new double[size];
            for (int i = 0; i < size; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);   // Hann.
            cosines = new double[size / 2];
            sines = new double[size / 2];
            for (int i = 0; i < size / 2; i++) { cosines[i] = Math.Cos(-2 * Math.PI * i / size); sines[i] = Math.Sin(-2 * Math.PI * i / size); }
            int bits = (int)Math.Round(Math.Log(size, 2));
            reversed = new int[size];
            for (int i = 0; i < size; i++)
            {
                int r = 0, v = i;
                for (int b = 0; b < bits; b++) { r = (r << 1) | (v & 1); v >>= 1; }
                reversed[i] = r;
            }
            re = new double[size];
            im = new double[size];
            magnitudes = new double[size / 2];
        }

        // Magnitudes of the first half of the spectrum of ring, read oldest first from start.
        // The returned array is reused by the next call.
        public double[] Magnitudes(double[] ring, int start)
        {
            for (int i = 0; i < size; i++)
            {
                re[reversed[i]] = ring[(start + i) % size] * window[i];
                im[reversed[i]] = 0;
            }
            for (int length = 2; length <= size; length *= 2)
            {
                int half = length / 2, step = size / length;
                for (int block = 0; block < size; block += length)
                    for (int j = 0; j < half; j++)
                    {
                        double wr = cosines[j * step], wi = sines[j * step];
                        int a = block + j, b = a + half;
                        double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                    }
            }
            for (int k = 0; k < size / 2; k++) magnitudes[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / size;
            return magnitudes;
        }
    }
}
