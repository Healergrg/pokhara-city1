// =============================================================================
//  PokharaCar.cs  —  the player's car (brand new for the city simulator)
// =============================================================================
//  How a Unity car works, in real-life terms:
//    * Rigidbody      = the car's weight and momentum (1,150 kg, like a small hatchback)
//    * WheelCollider  = a tyre + spring. It touches the road, grips, steers and brakes.
//    * This script    = the driver's feet and hands: it reads the keys and tells
//                       the wheels how hard to push, brake and turn.
//
//  Gearbox is AUTOMATIC: hold S to brake; once stopped, keep holding S to reverse.
//  Nepal drives on the LEFT; the driver sits on the RIGHT (see CarCamera).
//
//  Other scripts (road rules, missions) can read:
//    SpeedKmh, Gear, LeftIndicatorOn, RightIndicatorOn, IsBraking
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PokharaCar : MonoBehaviour
{
    [Header("Wheels (filled in by Tools > Pokhara City > Create Player Car)")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;
    public Transform frontLeftVisual, frontRightVisual, rearLeftVisual, rearRightVisual;

    [Header("Engine and brakes")]
    public float engineTorque = 1100f;     // push on each front wheel (front-wheel drive)
    public float brakeTorque = 2600f;
    public float handbrakeTorque = 6000f;
    public float topSpeedKmh = 80f;
    public float reverseTopSpeedKmh = 20f;

    [Header("Steering")]
    public float maxSteerAngle = 34f;       // degrees at walking speed
    public float steerAtTopSpeed = 0.35f;   // only 35% of that at top speed (stable at speed)
    public float steerSpeed = 120f;         // degrees per second the steering wheel turns

    [Header("Lights (filled in by the car builder)")]
    public GameObject[] leftIndicatorGlows;
    public GameObject[] rightIndicatorGlows;
    public GameObject[] brakeLightGlows;

    // ---- things other scripts can read ----
    public float SpeedKmh { get; private set; }   // + forward, - backward
    public string Gear { get; private set; } = "N";
    public bool LeftIndicatorOn { get; private set; }
    public bool RightIndicatorOn { get; private set; }
    public bool IsBraking { get; private set; }
    public int GearNumber { get; private set; } = 1;   // 1-5 when driving forward
    public float Rpm01 { get; private set; }            // engine revs, 0 = idle, 1 = red line

    private Rigidbody body;
    private bool reversing;
    private float currentSteer;
    private float blinkTimer;
    private float indicatorStartHeading;
    private AudioSource engineAudio, loadAudio, hornAudio, clickAudio, skidAudio, crashAudio;
    private float skidVolume;
    // A pretend 5-speed gearbox: the top speed (km/h) of each gear.
    // Like a bicycle with gears: each gear covers a range of speed, and the
    // engine revs climb inside that range, then drop when you change up.
    private static readonly float[] GearTop = { 18f, 32f, 47f, 62f, 999f };
    private bool lastBlinkState;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.mass = 1150f;
        body.centerOfMass = new Vector3(0f, 0.35f, 0.1f);   // low centre = doesn't tip over in turns
        body.interpolation = RigidbodyInterpolation.Interpolate;   // smooth movement for the camera

        // Sounds are made by maths here, so you don't need any audio files.
        engineAudio = MakeAudio(CarSounds.Engine(), true, 0.35f);
        loadAudio = MakeAudio(CarSounds.EngineLoad(), true, 0f);      // the "roar" when you press the gas
        hornAudio = MakeAudio(CarSounds.Horn(), true, 0.6f);
        clickAudio = MakeAudio(CarSounds.Click(), false, 0.5f);
        skidAudio = MakeAudio(CarSounds.Skid(), true, 0f);
        crashAudio = MakeAudio(CarSounds.Crash(), false, 0.8f);
        engineAudio.Play(); loadAudio.Play(); skidAudio.Play();

        // Pokhara street sounds (birds, distant horns, temple bells...).
        if (GetComponent<AmbientCity>() == null) gameObject.AddComponent<AmbientCity>();
    }

    // ---------------------------------------------------------------------
    // Update runs every frame: good for keys that are pressed once,
    // blinking lights, sounds and turning the wheel models.
    // ---------------------------------------------------------------------
    private void Update()
    {
        if (DriveInput.LeftIndicator()) SetIndicator(!LeftIndicatorOn, false);
        if (DriveInput.RightIndicator()) SetIndicator(false, !RightIndicatorOn);
        AutoCancelIndicator();
        BlinkIndicators();

        // Horn
        if (DriveInput.Horn() && !hornAudio.isPlaying) hornAudio.Play();
        if (!DriveInput.Horn() && hornAudio.isPlaying) hornAudio.Stop();

        UpdateEngineSound();
        UpdateSkidSound();

        // Brake lights
        foreach (GameObject g in brakeLightGlows) if (g != null) g.SetActive(IsBraking);

        // Make the wheel models follow the real (invisible) wheels.
        MatchWheel(frontLeft, frontLeftVisual);
        MatchWheel(frontRight, frontRightVisual);
        MatchWheel(rearLeft, rearLeftVisual);
        MatchWheel(rearRight, rearRightVisual);
    }

    // ---------------------------------------------------------------------
    // FixedUpdate runs at a steady rate for physics: pedals and steering.
    // ---------------------------------------------------------------------
    private void FixedUpdate()
    {
        // Forward speed: how fast we move in the direction the car is pointing.
        SpeedKmh = Vector3.Dot(body.linearVelocity, transform.forward) * 3.6f;

        float gas = DriveInput.Throttle();
        float brakePedal = DriveInput.Brake();
        float motor = 0f, brake = 0f;

        if (!reversing)
        {
            if (gas > 0f && SpeedKmh < topSpeedKmh) motor = gas * engineTorque;
            if (brakePedal > 0f)
            {
                if (SpeedKmh > 1f) brake = brakePedal * brakeTorque;   // slowing down
                else reversing = true;                                  // stopped: switch to R
            }
        }
        else
        {
            if (brakePedal > 0f && -SpeedKmh < reverseTopSpeedKmh) motor = -brakePedal * engineTorque * 0.6f;
            if (gas > 0f)
            {
                if (SpeedKmh < -1f) brake = gas * brakeTorque;          // stop the backwards roll first
                else reversing = false;                                 // stopped: back to D
            }
        }

        // No pedal at all: the engine gently slows the car (engine braking).
        if (gas == 0f && brakePedal == 0f) brake = 120f;

        IsBraking = brake > 200f || DriveInput.Handbrake();
        Gear = reversing ? "R" : (gas > 0f || Mathf.Abs(SpeedKmh) > 1f ? "D" : "N");

        // Front-wheel drive, like most small cars in Nepal.
        frontLeft.motorTorque = motor;
        frontRight.motorTorque = motor;
        frontLeft.brakeTorque = brake;
        frontRight.brakeTorque = brake;
        rearLeft.brakeTorque = DriveInput.Handbrake() ? handbrakeTorque : brake;
        rearRight.brakeTorque = DriveInput.Handbrake() ? handbrakeTorque : brake;

        // Steering: turn smoothly, and turn less when going fast.
        float speedFactor = Mathf.Lerp(1f, steerAtTopSpeed, Mathf.Abs(SpeedKmh) / topSpeedKmh);
        float target = DriveInput.Steer() * maxSteerAngle * speedFactor;
        currentSteer = Mathf.MoveTowards(currentSteer, target, steerSpeed * Time.fixedDeltaTime);
        frontLeft.steerAngle = currentSteer;
        frontRight.steerAngle = currentSteer;

        // A little push down at speed so the car stays planted on the road.
        body.AddForce(-transform.up * Mathf.Abs(SpeedKmh) * 25f);
    }

    // ---------------------------------------------------------------------
    // Sounds
    // ---------------------------------------------------------------------
    private void UpdateEngineSound()
    {
        float speed = Mathf.Abs(SpeedKmh);
        float gas = Gear == "R" ? DriveInput.Brake() : DriveInput.Throttle();

        if (Gear == "R")
        {
            GearNumber = 1;
            Rpm01 = Mathf.Clamp01(0.2f + speed / reverseTopSpeedKmh * 0.6f + gas * 0.1f);
        }
        else if (speed < 1f)
        {
            GearNumber = 1;
            Rpm01 = 0.12f + gas * 0.25f;   // idling; a little rev when you press the gas
        }
        else
        {
            // Which gear are we in? The first one whose top speed is above our speed.
            int g = 0;
            while (g < GearTop.Length - 1 && speed > GearTop[g]) g++;
            GearNumber = g + 1;
            // Inside the gear: from 35% revs just after changing up, to 100% at its top speed.
            float bottom = g == 0 ? 0f : GearTop[g - 1];
            float top = g == GearTop.Length - 1 ? topSpeedKmh + 5f : GearTop[g];
            Rpm01 = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(bottom, top, speed)) * (0.85f + 0.15f * gas);
        }

        float dt = Time.deltaTime;
        engineAudio.pitch = Mathf.Lerp(engineAudio.pitch, 0.55f + Rpm01 * 1.3f, 12f * dt);
        engineAudio.volume = 0.22f + 0.12f * gas + 0.1f * Rpm01;
        loadAudio.pitch = engineAudio.pitch;
        loadAudio.volume = Mathf.Lerp(loadAudio.volume, 0.16f * gas * (0.4f + Rpm01), 8f * dt);
    }

    // Tyres squeal when they slide (sharp turns, hard braking, handbrake).
    private void UpdateSkidSound()
    {
        float slip = 0f;
        foreach (WheelCollider w in new[] { frontLeft, frontRight, rearLeft, rearRight })
        {
            WheelHit hit;
            if (w != null && w.GetGroundHit(out hit))
                slip = Mathf.Max(slip, Mathf.Abs(hit.sidewaysSlip), Mathf.Abs(hit.forwardSlip) * 0.7f);
        }
        float wanted = Mathf.Abs(SpeedKmh) > 8f ? Mathf.Clamp01((slip - 0.3f) * 2f) * 0.5f : 0f;
        skidVolume = Mathf.MoveTowards(skidVolume, wanted, Time.deltaTime * 3f);
        skidAudio.volume = skidVolume;
        skidAudio.pitch = 0.9f + Mathf.Abs(SpeedKmh) / 200f;
    }

    // A thud when you hit something (louder for harder crashes).
    private void OnCollisionEnter(Collision collision)
    {
        float impact = collision.relativeVelocity.magnitude;
        if (impact < 3f || collision.contactCount == 0) return;
        // Bumping up a kerb or landing on the road is not a crash.
        if (Vector3.Dot(collision.GetContact(0).normal, Vector3.up) > 0.7f) return;
        crashAudio.volume = Mathf.Clamp01(impact / 14f);
        crashAudio.pitch = Random.Range(0.85f, 1.1f);
        crashAudio.Play();
    }

    // ---------------------------------------------------------------------
    // Indicators
    // ---------------------------------------------------------------------
    private void SetIndicator(bool left, bool right)
    {
        LeftIndicatorOn = left;
        RightIndicatorOn = right;
        indicatorStartHeading = transform.eulerAngles.y;
        blinkTimer = 0f;
        clickAudio.Play();
    }

    // Real cars switch the indicator off by themselves after the turn:
    // once we have turned more than 60 degrees and the steering is straight again.
    private void AutoCancelIndicator()
    {
        if (!LeftIndicatorOn && !RightIndicatorOn) return;
        float turned = Mathf.Abs(Mathf.DeltaAngle(indicatorStartHeading, transform.eulerAngles.y));
        if (turned > 60f && Mathf.Abs(DriveInput.Steer()) < 0.1f)
        {
            LeftIndicatorOn = false;
            RightIndicatorOn = false;
        }
    }

    // On for 0.4 s, off for 0.4 s, with a "tick" each time it switches.
    private void BlinkIndicators()
    {
        blinkTimer += Time.deltaTime;
        bool lit = (blinkTimer % 0.8f) < 0.4f;
        if ((LeftIndicatorOn || RightIndicatorOn) && lit != lastBlinkState) clickAudio.Play();
        lastBlinkState = lit;

        foreach (GameObject g in leftIndicatorGlows) if (g != null) g.SetActive(LeftIndicatorOn && lit);
        foreach (GameObject g in rightIndicatorGlows) if (g != null) g.SetActive(RightIndicatorOn && lit);
    }

    // True while the indicator lamp is lit (handy for the dashboard).
    public bool IndicatorLampLit => (blinkTimer % 0.8f) < 0.4f;

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------
    private static void MatchWheel(WheelCollider wheel, Transform visual)
    {
        if (wheel == null || visual == null) return;
        Vector3 position;
        Quaternion rotation;
        wheel.GetWorldPose(out position, out rotation);
        visual.SetPositionAndRotation(position, rotation);
    }

    private AudioSource MakeAudio(AudioClip clip, bool loop, float volume)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.volume = volume;
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D sound: same volume whatever the camera does
        return source;
    }
}

// =============================================================================
//  CarSounds — tiny sounds built from sine waves (no audio files needed).
//  A sound is just a list of numbers between -1 and 1, played very fast:
//  44,100 numbers per second.
// =============================================================================
public static class CarSounds
{
    private const int Rate = 44100;

    // A small petrol engine: one deep "putt" sound (45 Hz) plus its
    // overtones, with a gentle "chug" on top. Every tone has a whole number
    // of waves per second, so the 1-second sound loops without a click.
    public static AudioClip Engine()
    {
        int n = Rate;
        var data = new float[n];
        var rng = new System.Random(4);
        var phase = new float[13];
        for (int k = 1; k <= 12; k++) phase[k] = (float)rng.NextDouble() * 6.28f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float v = 0f;
            for (int k = 1; k <= 12; k++)
                v += Mathf.Sin(2f * Mathf.PI * 45f * k * t + phase[k]) / Mathf.Pow(k, 0.85f);
            float chug = 1f + 0.35f * Mathf.Sin(2f * Mathf.PI * 15f * t);   // cylinders firing
            data[i] = v * chug * 0.16f;
        }
        Blend(data, LoopNoise(n, 0.08f, 11), 0.10f);   // a little mechanical rattle
        return Make("Engine", data);
    }

    // The roar under load: rumbly noise that gets louder when you press the gas.
    public static AudioClip EngineLoad()
    {
        int n = Rate;
        float[] data = LoopNoise(n, 0.05f, 21);
        for (int i = 0; i < n; i++) data[i] *= 1f + 0.5f * Mathf.Sin(2f * Mathf.PI * 30f * i / Rate);
        Normalize(data, 0.7f);
        return Make("EngineLoad", data);
    }

    // Tyre squeal: a wobbly high tone mixed with hiss.
    public static AudioClip Skid()
    {
        int n = Rate;
        float[] hiss = LoopNoise(n, 0.6f, 31);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float wobble = 18f * Mathf.Sin(2f * Mathf.PI * 7f * t);
            data[i] = 0.45f * Mathf.Sin(2f * Mathf.PI * (820f * t) + wobble / 7f) + 0.35f * hiss[i];
        }
        Normalize(data, 0.6f);
        return Make("Skid", data);
    }

    // Crash: a deep thump and a crunch, fading out quickly.
    public static AudioClip Crash()
    {
        int n = Rate / 2;
        float[] crunch = LoopNoise(n, 0.35f, 41);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float thump = Mathf.Sin(2f * Mathf.PI * (70f - 40f * t) * t) * Mathf.Exp(-t * 9f);
            data[i] = 0.9f * thump + 0.6f * crunch[i] * Mathf.Exp(-t * 14f);
        }
        Normalize(data, 0.9f);
        return Make("Crash", data);
    }

    // Soft city hum (far-away traffic, people, fans), loops for 4 seconds.
    public static AudioClip CityHum()
    {
        int n = Rate * 4;
        float[] data = LoopNoise(n, 0.02f, 51);
        Normalize(data, 0.5f);
        return Make("CityHum", data);
    }

    // A small bird: two quick rising chirps.
    public static AudioClip Bird()
    {
        int n = Rate * 4 / 10;
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float local = t % 0.16f;                       // each chirp lasts 0.16 s
            float f = 2600f + 9000f * local;               // pitch slides up
            phase += 2f * Mathf.PI * f / Rate;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(local / 0.12f));
            data[i] = Mathf.Sin(phase) * env * 0.5f * (t < 0.32f ? 1f : 0f);
        }
        return Make("Bird", data);
    }

    // A temple bell: a few metal tones that ring and slowly fade.
    public static AudioClip Bell()
    {
        int n = Rate * 3;
        var data = new float[n];
        float[] f = { 523f, 1059f, 1571f, 2093f };
        float[] a = { 1f, 0.6f, 0.35f, 0.2f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, v = 0f;
            for (int k = 0; k < f.Length; k++) v += a[k] * Mathf.Sin(2f * Mathf.PI * f[k] * t) * Mathf.Exp(-t * (1.2f + k * 0.8f));
            data[i] = v * 0.4f;
        }
        return Make("Bell", data);
    }

    // ---- helpers for the sound recipes ----
    // Random noise, smoothed ("smooth" small = deep rumble, near 1 = sharp hiss),
    // with the end faded into the start so it loops without a click.
    private static float[] LoopNoise(int n, float smooth, int seed)
    {
        var rng = new System.Random(seed);
        int fade = Rate / 10;
        var raw = new float[n + fade];
        float y = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float x = (float)rng.NextDouble() * 2f - 1f;
            y += smooth * (x - y);     // a simple "low-pass filter": follows the noise slowly
            raw[i] = y;
        }
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = raw[i];
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] = raw[i] * k + raw[n + i] * (1f - k);   // crossfade the seam
        }
        Normalize(data, 1f);
        return data;
    }

    private static void Blend(float[] into, float[] add, float amount)
    {
        for (int i = 0; i < into.Length && i < add.Length; i++) into[i] += add[i] * amount;
    }

    private static void Normalize(float[] data, float peak)
    {
        float max = 0.0001f;
        foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
        for (int i = 0; i < data.Length; i++) data[i] = data[i] / max * peak;
    }

    // Two-tone horn, like most cars on Nepali roads.
    public static AudioClip Horn()
    {
        int n = Rate / 2;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float a = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 400f * t));   // square waves sound "brassy"
            float b = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 500f * t));
            data[i] = (a + b) * 0.25f;
        }
        return Make("Horn", data);
    }

    // Short "tick" of the indicator relay.
    public static AudioClip Click()
    {
        int n = Rate / 40;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float fade = 1f - i / (float)n;
            data[i] = Mathf.Sin(2f * Mathf.PI * 1800f * i / Rate) * fade * fade;
        }
        return Make("Click", data);
    }

    private static AudioClip Make(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
