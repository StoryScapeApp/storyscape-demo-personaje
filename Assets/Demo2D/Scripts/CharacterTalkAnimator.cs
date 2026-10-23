using UnityEngine;

/// <summary>
/// Anima la boca del personaje mientras habla el agente de ElevenLabs.
/// Elige el sprite según el volumen de la voz: 0 = cerrada, 1 = entreabierta, 2 = abierta.
/// En modo simulado mueve la boca con un patrón natural.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterTalkAnimator : MonoBehaviour
{
    public ElevenLabsAgentClient agent;

    [Tooltip("0 = boca cerrada, 1 = entreabierta, 2 = abierta (personaje mirando al lector)")]
    public Sprite[] talkFrames = new Sprite[3];

    [Tooltip("Cambios de boca por segundo")]
    public float frameRate = 12f;
    [Tooltip("Volumen (RMS) a partir del cual la boca se entreabre")]
    public float halfOpenLevel = 0.02f;
    [Tooltip("Volumen (RMS) a partir del cual la boca se abre del todo")]
    public float openLevel = 0.07f;

    SpriteRenderer sr;
    float timer;
    bool wasTalking;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (agent == null || talkFrames == null || talkFrames.Length < 3) return;

        if (!agent.IsRunning)
        {
            if (wasTalking) sr.sprite = talkFrames[0];
            wasTalking = false;
            return;
        }

        timer += Time.deltaTime;
        if (timer < 1f / frameRate) return;
        timer = 0f;

        int frame = 0;
        if (agent.IsAgentTalking)
        {
            float level = agent.SimulatedTalking
                ? Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 0.12f
                : agent.OutputLevel;
            frame = level >= openLevel ? 2 : level >= halfOpenLevel ? 1 : 0;
        }

        sr.sprite = talkFrames[frame];
        wasTalking = true;
    }
}
