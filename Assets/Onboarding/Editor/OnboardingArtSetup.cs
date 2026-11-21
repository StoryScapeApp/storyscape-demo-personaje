using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// StoryScape > Onboarding > Aplicar arte acuarela
/// Usa los sprites de Assets/Onboarding/Sprites/Acuarela (recortados de Assets/Demo2D/Onboarding/Sprites/raw):
/// fondo acuarela, título, las 3 tarjetas de "Cómo funciona", el zorro y el aterrizaje del Principito.
/// - El fondo cubre toda la proyección; un rectángulo negro sobre el libro evita proyectar luz encima de él.
/// - Al terminar el onboarding queda el fondo del prólogo (fondo_prologo, de raw/prologue_background.png).
/// - Las tarjetas aparecen una por una (regla 1, 2 y 3), igual que antes.
/// - En la tarjeta 3, las píldoras A y B se iluminan en la demo del quiz.
/// Coordenadas: cámara ortográfica de tamaño 4 (x de -7.1 a 7.1, y de -4 a 4); libro de 5.51 × 3.73 centrado.
/// </summary>
public static class OnboardingArtSetup
{
    const string Dir = "Assets/Onboarding/Sprites/Acuarela";
    // false = el fondo acuarela cubre toda la mesa, sin el espacio negro del libro en el medio
    const bool UsarMascaraLibro = false;

    // Escala de cada imagen (píxeles por unidad) calculada para la proyección 1920×1080
    // fondo_prologo: 1382×779 px cubriendo los 14,22 × 8 de la cámara
    const float PpuPrologo = 97.2f;
    const float PpuFondo = 97.625f, PpuTitulo = 165.556f, PpuTarjetas = 114.433f, PpuZorro = 75f, PpuAterrizaje = 130.841f;

    static readonly Vector3 TituloPos = new Vector3(0f, 2.98f, 0f);
    static readonly Vector3[] TarjetaPos = { new Vector3(-4.199f, -2.95f, 0f), new Vector3(-0.214f, -2.972f, 0f), new Vector3(3.98f, -2.95f, 0f) };
    // Píldoras A y B dentro de la tarjeta 3 (centro y tamaño)
    static readonly Vector3 PildoraA = new Vector3(5.422f, -2.95f, 0f), PildoraB = new Vector3(5.422f, -3.321f, 0f);
    static readonly Vector2 PildoraSize = new Vector2(0.80f, 0.40f);
    static readonly Vector3 PrincipitoPos = new Vector3(-4.9f, -1.6f, 0f);     // sobre la colina izquierda
    const float ZorroEscala = 0.78f;   // tamaño del zorro (1 = como en el dibujo original)
    static readonly Vector3 ZorroInicio = new Vector3(8.6f, -1.35f, 0f), ZorroDestino = new Vector3(5.1f, -1.35f, 0f);

    static readonly Vector2[] PivZorro =
    {
        new Vector2(.5f,.2331f), new Vector2(.5f,.2454f), new Vector2(.5f,.2393f), new Vector2(.5f,.2393f), new Vector2(.5f,.2393f), new Vector2(.5f,.2209f),
        new Vector2(.5f,.1902f), new Vector2(.5f,.1963f), new Vector2(.5f,.2025f), new Vector2(.5f,.2086f), new Vector2(.5f,.2147f), new Vector2(.5f,.2147f),
    };
    static readonly Vector2[] PivAterrizaje =
    {
        new Vector2(.498f,.0473f), new Vector2(.4833f,.0473f), new Vector2(.4661f,.0473f), new Vector2(.4596f,.0073f),
        new Vector2(.4721f,.04f), new Vector2(.4669f,.04f), new Vector2(.4517f,.0509f), new Vector2(.4667f,.0473f),
    };

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    [MenuItem("StoryScape/Onboarding/Aplicar arte acuarela")]
    public static void Menu()
    {
        if (Apply()) Debug.Log("[Onboarding] Arte acuarela aplicado: fondo, título, tarjetas, zorro y aterrizaje del Principito.");
    }

    public static void ImportSprites()
    {
        Import("fondo_acuarela", PpuFondo, new Vector2(.5f, .5f));
        Import("fondo_prologo", PpuPrologo, new Vector2(.5f, .5f));
        Import("titulo_onboarding", PpuTitulo, new Vector2(.5f, .5f));
        for (int i = 1; i <= 3; i++) Import("tarjeta_" + i, PpuTarjetas, new Vector2(.5f, .5f));
        Import("blanco", 8f, new Vector2(.5f, .5f));
        Import("mascara_suave", 100f, new Vector2(.5f, .5f));
        for (int i = 0; i < PivZorro.Length; i++) Import($"zorro_{i + 1:00}", PpuZorro, PivZorro[i]);
        for (int i = 0; i < PivAterrizaje.Length; i++) Import($"aterrizaje_{i + 1:00}", PpuAterrizaje, PivAterrizaje[i]);
    }

    static void Import(string name, float ppu, Vector2 pivot)
    {
        string path = $"{Dir}/{name}.png";
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[Onboarding] No se encontró " + path); return; }
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = ppu;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 2048;
        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.spriteAlignment = (int)SpriteAlignment.Custom;
        st.spritePivot = pivot;
        st.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(st);
        ti.SaveAndReimport();
    }

    static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{name}.png");

    public static bool Apply()
    {
        var seq = Object.FindFirstObjectByType<OnboardingSequence>(FindObjectsInactive.Include);
        if (!seq) { Debug.LogWarning("[Onboarding] Abre la escena Onboarding primero."); return false; }
        ImportSprites();

        // Rehacer el arte si ya existía
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t && t.parent == null && t.name == "ArteAcuarela") Object.DestroyImmediate(t.gameObject);
        // Apagar la versión anterior (título de texto, tarjetas de texto, círculos A/B)
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t && t.parent == null && (t.name == "Encabezado" || t.name == "Tarjetas" || t.name == "Quiz")) t.gameObject.SetActive(false);

        var root = new GameObject("ArteAcuarela").transform;

        // Fondo (toda la proyección) y máscara negra sobre el libro
        var fondo = Card("Fondo", root, Vector3.zero, S("fondo_acuarela"), Color.white, -100);
        // Fondo del prólogo: debajo del de acuarela, aparece cuando este se desvanece al final del onboarding
        var fondoPrologo = S("fondo_prologo") ? Card("FondoPrologo", root, Vector3.zero, S("fondo_prologo"), Color.white, -101) : null;
        Transform book = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "LibroReferencia") { book = t; break; }
        // Negro sólido sobre el libro y bordes que se difuminan hacia el fondo (sin un corte marcado)
        var mask = Card("MascaraLibro", root, book ? book.position : Vector3.zero, S("mascara_suave"), Color.black, -50);
        mask.gameObject.SetActive(UsarMascaraLibro);

        // Título y tarjetas
        var titulo = Card("Titulo", root, TituloPos, S("titulo_onboarding"), Color.white, 4);
        var tarjetas = new ProjectedCard[3];
        for (int i = 0; i < 3; i++) tarjetas[i] = Card("Tarjeta_" + (i + 1), root, TarjetaPos[i], S("tarjeta_" + (i + 1)), Color.white, 4);

        // Píldoras A y B de la tarjeta 3 (demo del quiz)
        var fill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Onboarding/Sprites/card_fondo.png");
        var border = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Onboarding/Sprites/card_borde.png");
        var pildoraA = Pill("Pildora_A", root, PildoraA, Hex("#4FB3E8"), fill, border);
        var pildoraB = Pill("Pildora_B", root, PildoraB, Hex("#F7C948"), fill, border);

        // Principito: sobre la colina izquierda + sprite del aterrizaje en el mismo lugar
        if (seq.principito)
        {
            seq.principito.transform.position = PrincipitoPos;
            EditorUtility.SetDirty(seq.principito.transform);
        }
        var landGo = new GameObject("Aterrizaje");
        landGo.transform.SetParent(root, false);
        landGo.transform.position = PrincipitoPos;
        var landSr = landGo.AddComponent<SpriteRenderer>();
        landSr.sortingOrder = 10;
        var landFrames = new Sprite[PivAterrizaje.Length];
        for (int i = 0; i < landFrames.Length; i++) landFrames[i] = S($"aterrizaje_{i + 1:00}");
        landSr.sprite = landFrames[landFrames.Length - 1];
        landSr.color = new Color(1f, 1f, 1f, 0f);

        // Zorro
        var foxGo = new GameObject("Zorro");
        foxGo.transform.SetParent(root, false);
        foxGo.transform.position = ZorroDestino;
        foxGo.transform.localScale = Vector3.one * ZorroEscala;
        var foxSr = foxGo.AddComponent<SpriteRenderer>();
        foxSr.sortingOrder = 9;
        foxSr.color = new Color(1f, 1f, 1f, 0f);
        var fox = foxGo.AddComponent<SpriteAnimator2D>();
        fox.bobAmplitude = 0f;
        fox.defaultClip = "Quieto";
        Sprite Z(int n) => S($"zorro_{n:00}");
        fox.clips = new List<SpriteAnimator2D.Clip>
        {
            new SpriteAnimator2D.Clip { name = "Caminar", frames = new[] { Z(1), Z(2), Z(3), Z(4), Z(5), Z(6) }, fps = 9f, loop = true },
            new SpriteAnimator2D.Clip { name = "Sentarse", frames = new[] { Z(12), Z(11), Z(10), Z(9) }, fps = 7f, loop = false, next = "Sentado" },
            // Un solo ciclo sentado (mira hacia arriba) y luego queda quieto en zorro_08
            new SpriteAnimator2D.Clip { name = "Sentado", frames = new[] { Z(8), Z(9), Z(8), Z(9), Z(7), Z(7) }, fps = 1.5f, loop = false, next = "Quieto" },
            new SpriteAnimator2D.Clip { name = "Quieto", frames = new[] { Z(8) }, fps = 1f, loop = true },
        };
        foxSr.sprite = Z(8);

        // Secuencia
        seq.fondo = fondo;
        seq.fondoFinal = fondoPrologo;
        seq.mascaraLibro = UsarMascaraLibro ? mask : null;
        seq.title = titulo;
        seq.subtitle = null;
        seq.cardBook = tarjetas[0];
        seq.cardRead = tarjetas[1];
        seq.cardQuiz = tarjetas[2];
        seq.options = new[] { pildoraA, pildoraB };
        seq.wrongOption = 0;
        seq.rightOption = 1;
        seq.aterrizaje = landSr;
        seq.framesAterrizaje = landFrames;
        seq.zorro = fox;
        seq.zorroInicio = ZorroInicio;
        // El zorro aparece desde detrás del planeta derecho: máscara con el borde del planeta (mascara_zorro_planeta)
        var mascaraSprite = S("mascara_zorro_planeta");
        if (mascaraSprite)
        {
            var mGo = new GameObject("MascaraZorro");
            mGo.transform.SetParent(root, false);
            mGo.transform.position = new Vector3(5.4f, -1.5f, 0f);
            var mascaraZorro = mGo.AddComponent<SpriteMask>();
            mascaraZorro.sprite = mascaraSprite;
            seq.mascaraPlaneta = mascaraZorro;
            seq.zorroDesdePlaneta = true;
        }
        seq.zorroDestino = ZorroDestino;
        // Estrella fugaz (regla 2): del planeta anillado al planeta azul del fondo, pasando entre el título y el libro
        seq.mostrarMeteoro = true;
        seq.planet = null;            // el fondo ya trae los planetas
        SetMeteorPath(seq.meteor);
        if (seq.stars) seq.stars.radius = 0.7f;
        EditorUtility.SetDirty(seq);

        EditorSceneManager.MarkSceneDirty(seq.gameObject.scene);
        EditorSceneManager.SaveScene(seq.gameObject.scene);
        return true;
    }

    static readonly Vector3[] MeteorPath =
    {
        new Vector3(-5.9f, 2.95f, 0f), new Vector3(-4.3f, 2.4f, 0f), new Vector3(-1.6f, 2.03f, 0f),
        new Vector3(1.6f, 2.03f, 0f), new Vector3(4.2f, 2.42f, 0f), new Vector3(5.75f, 2.8f, 0f),
    };

    static void SetMeteorPath(MeteorFlight meteor)
    {
        if (!meteor || meteor.waypoints == null) return;
        // Más grande, dorada y con contorno azul oscuro para que se distinga sobre el fondo acuarela
        meteor.headSize = 0.8f;
        meteor.headColor = new Color(1f, 0.72f, 0.12f);
        meteor.trailColor = new Color(1f, 0.8f, 0.3f);
        meteor.trailSize = 0.26f;
        meteor.trailLife = 0.9f;
        meteor.trailInterval = 0.025f;
        meteor.outlineColor = new Color(0.12f, 0.16f, 0.38f, 0.75f);
        meteor.outlineScale = 1.3f;
        EditorUtility.SetDirty(meteor);
        for (int i = 0; i < meteor.waypoints.Length && i < MeteorPath.Length; i++)
            if (meteor.waypoints[i]) { meteor.waypoints[i].position = MeteorPath[i]; EditorUtility.SetDirty(meteor.waypoints[i]); }
    }

    static ProjectedCard Card(string name, Transform parent, Vector3 pos, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        var card = go.AddComponent<ProjectedCard>();
        card.fill = sr;
        card.color = color;
        card.fillAlpha = 1f;
        card.fadeSpeed = 2f;
        return card;
    }

    static ProjectedCard Pill(string name, Transform parent, Vector3 pos, Color color, Sprite fillSprite, Sprite borderSprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var f = new GameObject("Fondo").AddComponent<SpriteRenderer>();
        f.transform.SetParent(go.transform, false);
        f.sprite = fillSprite; f.drawMode = SpriteDrawMode.Sliced; f.size = PildoraSize; f.sortingOrder = 6;
        var b = new GameObject("Borde").AddComponent<SpriteRenderer>();
        b.transform.SetParent(go.transform, false);
        b.sprite = borderSprite; b.drawMode = SpriteDrawMode.Sliced; b.size = PildoraSize; b.sortingOrder = 7;
        var card = go.AddComponent<ProjectedCard>();
        card.fill = f;
        card.border = b;
        card.color = color;
        card.fillAlpha = 0.15f;
        return card;
    }
}
