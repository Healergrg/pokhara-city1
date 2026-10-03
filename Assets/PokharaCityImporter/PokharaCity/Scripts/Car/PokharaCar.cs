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

    private Rigidbody body;
    private bool reversing;
    private float currentSteer;
    private float blinkTimer;
    private float indicatorStartHeading;
    private AudioSource engineAudio, hornAudio, clickAudio;
    private bool lastBlinkState;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.mass = 1150f;
        body.centerOfMass = new Vector3(0f, 0.35f, 0.1f);   // low centre = doesn't tip over in turns
        body.interpolation = RigidbodyInterpolation.Interpolate;   // smooth movement for the camera

        // Sounds are made by maths here, so you don't need any audio files.
        engineAudio = MakeAudio(CarSounds.Engine(), true, 0.35f);
        hornAudio = MakeAudio(CarSounds.Horn(), true, 0.6f);
        clickAudio = MakeAudio(CarSounds.Click(), false, 0.5f);
        engineAudio.Play();
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

        // Engine sound gets higher as the car goes faster.
        engineAudio.pitch = 0.7f + Mathf.Abs(SpeedKmh) / topSpeedKmh * 1.3f + DriveInput.Throttle() * 0.15f;

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

    // Low humming engine: a few deep tones mixed together, loops smoothly.
    public static AudioClip Engine()
    {
        int n = Rate;   // 1 second, loops
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            data[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 50f * t)
                    + 0.3f * Mathf.Sin(2f * Mathf.PI * 100f * t)
                    + 0.15f * Mathf.Sin(2f * Mathf.PI * 150f * t);
            data[i] *= 0.6f;
        }
        return Make("Engine", data);
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
