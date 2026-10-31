using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animador simple por cambio de sprites (Sprite Swap) con clips con nombre:
/// Idle, Listening, Asking, Nod, React…  Play("Listening") cambia de animación.
/// Los clips sin loop vuelven al clip por defecto al terminar.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteAnimator2D : MonoBehaviour
{
    [Serializable]
    public class Clip
    {
        public string name;
        public Sprite[] frames;
        public float fps = 8f;
        public bool loop = true;
    }

    public List<Clip> clips = new List<Clip>();
    public string defaultClip = "Idle";

    [Header("Respiración (movimiento sutil)")]
    public float bobAmplitude = 0.04f;
    public float bobSpeed = 2f;

    public string CurrentClip => current != null ? current.name : null;

    SpriteRenderer sr;
    Clip current;
    float timer;
    int frame;
    Vector3 basePos;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        basePos = transform.localPosition;
    }

    void Start()
    {
        if (current == null) Play(defaultClip);
    }

    public void Play(string clipName, bool restart = false)
    {
        var c = clips.Find(x => x.name == clipName);
        if (c == null || c.frames == null || c.frames.Length == 0)
        {
            Debug.LogWarning($"[SpriteAnimator2D] No existe el clip \"{clipName}\" o no tiene sprites.");
            return;
        }
        if (c == current && !restart) return;

        current = c;
        timer = 0f;
        frame = 0;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.sprite = c.frames[0];
    }

    void Update()
    {
        if (bobAmplitude > 0f)
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmplitude);

        if (current == null || current.frames.Length <= 1) return;

        timer += Time.deltaTime;
        float frameTime = 1f / Mathf.Max(0.01f, current.fps);
        while (timer >= frameTime)
        {
            timer -= frameTime;
            frame++;
            if (frame >= current.frames.Length)
            {
                if (current.loop) frame = 0;
                else
                {
                    Play(defaultClip);
                    return;
                }
            }
        }
        sr.sprite = current.frames[frame];
    }
}
