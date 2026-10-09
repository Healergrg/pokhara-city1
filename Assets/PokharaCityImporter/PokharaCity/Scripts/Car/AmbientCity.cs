// =============================================================================
//  AmbientCity.cs  —  the sounds of Pokhara around you
// =============================================================================
//  A city is never silent. This plays, quietly in the background:
//    - a soft city hum (far-away traffic, people, shop fans)
//    - birds chirping now and then (Lakeside has lots of trees)
//    - distant horns (it IS Nepal...)
//    - once in a while, a temple bell
//
//  Every sound is made by maths in CarSounds (no audio files), and each one
//  comes at a random moment with a random pitch, so it never sounds like
//  the same recording on repeat. PokharaCar adds this script by itself.
// =============================================================================

using UnityEngine;

public class AmbientCity : MonoBehaviour
{
    [Range(0f, 1f)] public float loudness = 1f;

    private AudioSource hum, oneShots, horn;
    private AudioClip bird, bell;
    private float nextBird, nextHorn, nextBell, hornStopAt;

    private void Start()
    {
        hum = Make(CarSounds.CityHum(), true, 0.10f * loudness);
        hum.Play();
        oneShots = Make(null, false, 1f);
        horn = Make(CarSounds.Horn(), true, 0f);
        bird = CarSounds.Bird();
        bell = CarSounds.Bell();

        nextBird = Time.time + Random.Range(2f, 6f);
        nextHorn = Time.time + Random.Range(5f, 12f);
        nextBell = Time.time + Random.Range(30f, 60f);
    }

    private void Update()
    {
        float now = Time.time;   // stops while the game is paused

        if (now > nextBird)
        {
            oneShots.pitch = Random.Range(0.85f, 1.25f);
            oneShots.PlayOneShot(bird, Random.Range(0.06f, 0.16f) * loudness);
            nextBird = now + Random.Range(3f, 9f);
        }

        if (now > nextHorn)
        {
            // A far-away car: quiet, a random pitch, a short or a long "peeep".
            horn.pitch = Random.Range(0.75f, 1.3f);
            horn.volume = Random.Range(0.03f, 0.08f) * loudness;
            horn.Play();
            hornStopAt = now + Random.Range(0.15f, 0.6f);
            nextHorn = now + Random.Range(6f, 18f);
        }
        if (horn.isPlaying && now > hornStopAt) horn.Stop();

        if (now > nextBell)
        {
            oneShots.pitch = Random.Range(0.95f, 1.05f);
            oneShots.PlayOneShot(bell, 0.12f * loudness);
            nextBell = now + Random.Range(45f, 100f);
        }
    }

    private AudioSource Make(AudioClip clip, bool loop, float volume)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.volume = volume;
        s.playOnAwake = false;
        s.spatialBlend = 0f;   // all around you, not from one spot
        return s;
    }
}
