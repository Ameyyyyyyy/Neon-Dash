using UnityEngine;
using System.Collections.Generic;

// =====================================================================
//  NEON DASH  -  a complete endless runner in ONE script
//  Attach this script to an Empty GameObject and press Play.
//  Everything (world, player, sounds, music, menus) is made by code.
// =====================================================================

// ---------- small helper classes ----------
public class Thing            // an obstacle or a pickup on the track
{
    public GameObject go;
    public int kind;
    public bool obstacle;
    public float y0, y1, halfW, halfD, vx, spin;
    public Vector3 baseScale;
}

public class Shard            // a little piece that flies out when something breaks
{
    public GameObject go;
    public Vector3 vel;
    public float life, maxLife, size;
}

public class FloatText        // "+10" that rises from a collected coin
{
    public string text;
    public Vector3 pos;
    public float life;
    public Color color;
}

public class NeonDash : MonoBehaviour
{
    // ---------- constants ----------
    const int K_COIN = 0, K_SHIELD = 1, K_MAGNET = 2, K_DOUBLE = 3, K_HEART = 4;
    const int K_LOW = 10, K_HIGH = 11, K_TALL = 12;
    const float SpawnZ = 85f;      // how far ahead things appear
    const float Gravity = 32f;
    const float JumpSpeed = 10.5f;

    // ---------- game state ----------
    enum State { Menu, Playing, GameOver }
    State state = State.Menu;
    bool paused, muted, pendingEnd;

    // ---------- modes: CHILL / NORMAL / INSANE ----------
    int mode = 1;
    readonly string[] modeName = { "CHILL", "NORMAL", "INSANE" };
    readonly int[] modeLives = { 5, 3, 1 };
    readonly float[] modeBase = { 12f, 15f, 19f };   // starting speed
    readonly float[] modeMax = { 26f, 32f, 40f };    // top speed

    // ---------- colour themes (change every level) ----------
    readonly Color[] themeCol =
    {
        new Color(0.10f, 0.60f, 1.00f),   // blue
        new Color(1.00f, 0.20f, 0.80f),   // pink
        new Color(0.20f, 1.00f, 0.40f),   // green
        new Color(1.00f, 0.60f, 0.10f),   // orange
        new Color(0.60f, 0.30f, 1.00f)    // purple
    };
    readonly Color[] themeBg =
    {
        new Color(0.02f, 0.03f, 0.09f),
        new Color(0.09f, 0.02f, 0.08f),
        new Color(0.02f, 0.08f, 0.04f),
        new Color(0.09f, 0.04f, 0.01f),
        new Color(0.05f, 0.02f, 0.10f)
    };
    Color curCol, curBg;

    // ---------- scene objects ----------
    Camera cam;
    Vector3 camPos;
    Material baseMat, matGrid, matPillar, matFloor, matStreak;
    Material matLow, matHigh, matTall, matStripe;
    Material matCoin, matShield, matMagnet, matDouble, matHeart;
    Material matBody, matHead, matPack, matLeg;
    Dictionary<int, Material> matCache = new Dictionary<int, Material>();

    List<Transform> crossLines = new List<Transform>();
    List<Transform> pillars = new List<Transform>();
    List<Transform> streaks = new List<Transform>();
    List<Thing> things = new List<Thing>();
    List<Shard> shards = new List<Shard>();
    List<FloatText> texts = new List<FloatText>();

    // ---------- player ----------
    GameObject player, shieldRing, magnetRing;
    Transform legL, legR;
    Renderer[] playerRenderers;
    int lane = 1;
    float px, py, vy;
    bool grounded = true, slideQueued;
    float slideTimer, runPhase, demoTimer;

    // ---------- run data ----------
    float speed, currentWS = 9f, distance, slowFactor = 1f;
    int bonus, coins, lives, maxLives, level = 1, coinStreak;
    int bestScore;
    bool newBest;
    float invincible, magnetTimer, doubleTimer;
    bool shield;
    float spawnAcc, nextGap, pendingExtra;
    float bannerTimer, shakeTime, flashAlpha;
    Color flashColor = Color.red;

    // ---------- input ----------
    Vector2 swipeStart;
    bool swiping;

    // ---------- audio ----------
    AudioSource sfx, music;
    AudioClip cJump, cSlide, cCoin, cPower, cHit, cLevel, cShield, cHeart, cLane, cMusic;

    int Score { get { return Mathf.FloorToInt(distance * 2f) + bonus; } }
    int Mult() { return Mathf.Min(1 + coinStreak / 10, 4); }
    float LaneX(int l) { return (l - 1) * 2f; }
    float CurHeight() { return slideTimer > 0f ? 0.7f : 1.6f; }

    // =================================================================
    //  START
    // =================================================================
    void Start()
    {
        Application.targetFrameRate = 60;

        cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 62f;
        camPos = new Vector3(0f, 4.3f, -7.5f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 35f;
        RenderSettings.fogEndDistance = 110f;

        // grab the default material so it works in any render pipeline
        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseMat = tmp.GetComponent<Renderer>().sharedMaterial;
        Destroy(tmp);

        curCol = themeCol[0];
        curBg = themeBg[0];

        matGrid = MakeMat(curCol);
        matPillar = MakeMat(Dark(curCol, 0.35f));
        matFloor = MakeMat(Dark(curCol, 0.06f));
        matStreak = MakeMat(new Color(0.8f, 0.9f, 1f));
        matLow = MakeMat(new Color(1f, 0.2f, 0.15f));
        matHigh = MakeMat(new Color(1f, 0.15f, 0.7f));
        matTall = MakeMat(new Color(0.35f, 0.3f, 1f));
        matStripe = MakeMat(new Color(1f, 1f, 0.9f));
        matCoin = MakeMat(new Color(1f, 0.85f, 0.1f));
        matShield = MakeMat(new Color(0.2f, 0.9f, 1f));
        matMagnet = MakeMat(new Color(1f, 0.55f, 0.1f));
        matDouble = MakeMat(new Color(0.2f, 1f, 0.3f));
        matHeart = MakeMat(new Color(1f, 0.5f, 0.6f));
        matBody = MakeMat(new Color(0.15f, 0.85f, 1f));
        matHead = MakeMat(new Color(0.85f, 0.95f, 1f));
        matPack = MakeMat(new Color(1f, 0.9f, 0.2f));
        matLeg = MakeMat(new Color(0.1f, 0.3f, 0.6f));

        BuildWorld();
        BuildPlayer();
        BuildAudio();

        bestScore = PlayerPrefs.GetInt("NDBest" + mode, 0);
        music.Play();
    }

    // =================================================================
    //  MATERIAL + OBJECT HELPERS
    // =================================================================
    Material MakeMat(Color c)
    {
        Material m = new Material(baseMat);
        m.color = c;
        return m;
    }

    // one shared material per colour (keeps memory low)
    Material ColMat(Color c)
    {
        Color32 k = c;
        int key = (k.r << 24) | (k.g << 16) | (k.b << 8) | k.a;
        Material m;
        if (!matCache.TryGetValue(key, out m))
        {
            m = MakeMat(c);
            matCache[key] = m;
        }
        return m;
    }

    Color Dark(Color c, float k)
    {
        return new Color(c.r * k, c.g * k, c.b * k, 1f);
    }

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Collider c = g.GetComponent<Collider>();
        if (c != null) Destroy(c);
        if (parent != null)
        {
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
        }
        else
        {
            g.transform.position = pos;
        }
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    // =================================================================
    //  BUILD THE WORLD
    // =================================================================
    void BuildWorld()
    {
        // dark floor
        Prim(PrimitiveType.Cube, new Vector3(0f, -0.06f, 55f), new Vector3(40f, 0.1f, 200f), matFloor);

        // glowing lane lines
        for (int i = -3; i <= 3; i += 2)
            Prim(PrimitiveType.Cube, new Vector3(i, 0.01f, 55f), new Vector3(0.07f, 0.02f, 200f), matGrid);

        // side edge lines
        Prim(PrimitiveType.Cube, new Vector3(-4.6f, 0.01f, 55f), new Vector3(0.12f, 0.02f, 200f), matGrid);
        Prim(PrimitiveType.Cube, new Vector3(4.6f, 0.01f, 55f), new Vector3(0.12f, 0.02f, 200f), matGrid);

        // cross lines that scroll toward the player (this gives the feeling of speed)
        for (int i = 0; i < 32; i++)
        {
            GameObject g = Prim(PrimitiveType.Cube, new Vector3(0f, 0.015f, -8f + i * 4f), new Vector3(9.2f, 0.02f, 0.07f), matGrid);
            crossLines.Add(g.transform);
        }

        // city pillars on both sides
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 12; i++)
            {
                float h = Random.Range(3f, 15f);
                float x = side * Random.Range(7.5f, 12f);
                GameObject root = new GameObject("Pillar");
                root.transform.position = new Vector3(x, 0f, -10f + i * 11f);
                Prim(PrimitiveType.Cube, new Vector3(0f, h / 2f, 0f), new Vector3(2.2f, h, 2.2f), matPillar, root.transform);
                Prim(PrimitiveType.Cube, new Vector3(-side * 1.15f, h / 2f, 0f), new Vector3(0.08f, h, 0.35f), matGrid, root.transform);
                pillars.Add(root.transform);
            }
        }

        // speed streaks
        for (int i = 0; i < 26; i++)
        {
            GameObject g = Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.03f, 0.03f, 1.6f), matStreak);
            g.transform.position = new Vector3(Random.Range(-12f, 12f), Random.Range(0.3f, 8f), Random.Range(0f, 60f));
            streaks.Add(g.transform);
        }
    }

    // =================================================================
    //  BUILD THE PLAYER (a little neon robot)
    // =================================================================
    void BuildPlayer()
    {
        player = new GameObject("Player");
        Transform t = player.transform;

        Prim(PrimitiveType.Cube, new Vector3(0f, 1.0f, 0f), new Vector3(0.8f, 0.8f, 0.5f), matBody, t);   // body
        Prim(PrimitiveType.Cube, new Vector3(0f, 1.6f, 0f), new Vector3(0.55f, 0.5f, 0.5f), matHead, t);  // head
        Prim(PrimitiveType.Cube, new Vector3(0f, 1.05f, -0.28f), new Vector3(0.5f, 0.14f, 0.06f), matPack, t); // glowing back stripe
        Prim(PrimitiveType.Cube, new Vector3(0f, 1.62f, -0.27f), new Vector3(0.3f, 0.1f, 0.05f), matPack, t);

        legL = Prim(PrimitiveType.Cube, new Vector3(-0.2f, 0.3f, 0f), new Vector3(0.28f, 0.6f, 0.3f), matLeg, t).transform;
        legR = Prim(PrimitiveType.Cube, new Vector3(0.2f, 0.3f, 0f), new Vector3(0.28f, 0.6f, 0.3f), matLeg, t).transform;

        playerRenderers = player.GetComponentsInChildren<Renderer>();

        // shield ring (little cubes circling the player)
        shieldRing = new GameObject("ShieldRing");
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.PI * 2f / 10f;
            Prim(PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 1.1f, 0f, Mathf.Sin(a) * 1.1f),
                 new Vector3(0.2f, 0.2f, 0.2f), matShield, shieldRing.transform);
        }
        shieldRing.SetActive(false);

        // magnet ring
        magnetRing = new GameObject("MagnetRing");
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            Prim(PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 1.6f, 0f, Mathf.Sin(a) * 1.6f),
                 new Vector3(0.22f, 0.22f, 0.22f), matMagnet, magnetRing.transform);
        }
        magnetRing.SetActive(false);
    }

    // =================================================================
    //  AUDIO (all sounds and the music are generated by code)
    // =================================================================
    void BuildAudio()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        music = gameObject.AddComponent<AudioSource>();
        music.loop = true;
        music.volume = 0.25f;

        cJump = Sweep(300f, 700f, 0.18f, 0.35f, true);
        cSlide = Noise(0.25f, 0.5f);
        cCoin = Arp(new float[] { 987.77f, 1318.5f }, 0.07f, 0.3f);
        cPower = Arp(new float[] { 523f, 659f, 784f, 1047f }, 0.08f, 0.3f);
        cHit = Sweep(220f, 50f, 0.45f, 0.6f, true);
        cLevel = Arp(new float[] { 523f, 659f, 784f, 1047f, 1319f }, 0.09f, 0.3f);
        cShield = Sweep(900f, 300f, 0.3f, 0.4f, false);
        cHeart = Arp(new float[] { 659f, 880f, 1175f }, 0.09f, 0.3f);
        cLane = Sweep(400f, 520f, 0.06f, 0.15f, false);
        cMusic = BuildMusic();
        music.clip = cMusic;
    }

    AudioClip MakeClip(string n, float[] d)
    {
        AudioClip c = AudioClip.Create(n, d.Length, 1, 44100, false);
        c.SetData(d, 0);
        return c;
    }

    AudioClip Sweep(float f0, float f1, float dur, float vol, bool square)
    {
        int n = (int)(44100 * dur);
        float[] d = new float[n];
        float ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            ph += 2f * Mathf.PI * f / 44100f;
            float s = Mathf.Sin(ph);
            if (square) s = Mathf.Sign(s) * 0.6f;
            d[i] = s * (1f - t) * vol * Mathf.Min(1f, i / 40f);
        }
        return MakeClip("sweep", d);
    }

    AudioClip Noise(float dur, float vol)
    {
        int n = (int)(44100 * dur);
        float[] d = new float[n];
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            prev = Mathf.Lerp(prev, Random.value * 2f - 1f, 0.35f);
            d[i] = prev * (1f - t) * vol;
        }
        return MakeClip("noise", d);
    }

    AudioClip Arp(float[] fr, float noteDur, float vol)
    {
        int nn = (int)(44100 * noteDur);
        float[] d = new float[nn * fr.Length];
        for (int k = 0; k < fr.Length; k++)
        {
            for (int i = 0; i < nn; i++)
            {
                float t = (float)i / nn;
                float tt = (float)i / 44100f;
                d[k * nn + i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * fr[k] * tt)) * 0.5f * vol * (1f - t) * Mathf.Min(1f, i / 40f);
            }
        }
        return MakeClip("arp", d);
    }

    // A small looping synthwave tune: bass + arpeggio + kick + hi-hat (A minor)
    AudioClip BuildMusic()
    {
        int rate = 44100;
        int steps = 16;
        int stepN = (int)(rate * 0.2f);
        int chordN = stepN * 4;
        float[] d = new float[stepN * steps];

        float[] lead =
        {
            440f, 523.25f, 659.25f, 523.25f,
            349.23f, 440f, 523.25f, 440f,
            523.25f, 659.25f, 783.99f, 659.25f,
            392f, 493.88f, 587.33f, 493.88f
        };
        float[] bass = { 110f, 87.31f, 130.81f, 98f };

        for (int s = 0; s < steps; s++)
        {
            float lf = lead[s];
            float bf = bass[s / 4];
            for (int i = 0; i < stepN; i++)
            {
                int idx = s * stepN + i;
                float t = (float)i / rate;
                float tt = (float)idx / rate;
                float v = 0f;

                // lead arpeggio
                float edgeL = Mathf.Min(1f, i / 60f) * Mathf.Min(1f, (stepN - i) / 120f);
                v += Mathf.Sign(Mathf.Sin(2f * Mathf.PI * lf * tt)) * 0.5f * Mathf.Exp(-t * 12f) * 0.16f * edgeL;

                // bass
                int ci = idx % chordN;
                float edgeB = Mathf.Min(1f, ci / 60f) * Mathf.Min(1f, (chordN - ci) / 120f);
                v += Mathf.Sin(2f * Mathf.PI * bf * tt) * 0.3f * edgeB;

                // kick drum on every second step
                if (s % 2 == 0)
                {
                    float ph = 2f * Mathf.PI * (45f * t + 110f * (1f - Mathf.Exp(-25f * t)) / 25f);
                    v += Mathf.Sin(ph) * Mathf.Exp(-t * 14f) * 0.45f;
                }
                // hi-hat on the other steps
                else if (i < rate * 0.05f)
                {
                    v += (Random.value * 2f - 1f) * Mathf.Exp(-t * 70f) * 0.12f;
                }

                d[idx] = Mathf.Clamp(v, -1f, 1f);
            }
        }
        return MakeClip("music", d);
    }

    void Play(AudioClip c, float pitch = 1f, float vol = 1f)
    {
        sfx.pitch = pitch;
        sfx.PlayOneShot(c, vol);
    }

    // =================================================================
    //  GAME FLOW
    // =================================================================
    void StartGame(int m)
    {
        mode = m;
        ClearThings();
        ClearShards();
        texts.Clear();

        maxLives = modeLives[m];
        lives = maxLives;
        coins = 0;
        bonus = 0;
        distance = 0f;
        coinStreak = 0;
        level = 1;
        speed = modeBase[m];
        slowFactor = 1f;
        invincible = 0f;
        shield = false;
        magnetTimer = 0f;
        doubleTimer = 0f;
        bannerTimer = 0f;

        lane = 1;
        px = 0f;
        py = 0f;
        vy = 0f;
        grounded = true;
        slideTimer = 0f;
        slideQueued = false;

        bestScore = PlayerPrefs.GetInt("NDBest" + m, 0);
        newBest = false;
        nextGap = 10f;
        spawnAcc = nextGap;
        pendingExtra = 0f;

        paused = false;
        Time.timeScale = 1f;
        player.SetActive(true);
        state = State.Playing;
    }

    void EndGame()
    {
        state = State.GameOver;
        paused = false;
        Time.timeScale = 1f;

        SpawnShards(new Vector3(px, py + 1f, 0f), new Color(0.2f, 0.9f, 1f), 30, 8f, 0.3f);
        SpawnShards(new Vector3(px, py + 1f, 0f), Color.white, 12, 6f, 0.2f);
        player.SetActive(false);
        ClearThings();
        Play(cHit);

        int s = Score;
        if (s > bestScore)
        {
            bestScore = s;
            newBest = true;
            PlayerPrefs.SetInt("NDBest" + mode, bestScore);
            PlayerPrefs.Save();
        }
    }

    void GoToMenu()
    {
        state = State.Menu;
        player.SetActive(true);
        lane = 1;
        level = 1;
        ClearShards();
        ClearThings();
    }

    void ClearThings()
    {
        foreach (Thing t in things)
            if (t.go != null) Destroy(t.go);
        things.Clear();
    }

    void ClearShards()
    {
        foreach (Shard s in shards)
            if (s.go != null) Destroy(s.go);
        shards.Clear();
    }

    // =================================================================
    //  UPDATE
    // =================================================================
    void Update()
    {
        float dt = Time.deltaTime;

        HandleInput();
        UpdatePlayer(dt);

        pendingEnd = false;
        if (state == State.Playing)
        {
            if (!paused) GameplayUpdate(dt);
        }
        else
        {
            DemoAI(dt);
            currentWS = Mathf.MoveTowards(currentWS, 9f, 30f * dt);
        }
        if (pendingEnd) EndGame();

        UpdateWorld(dt, currentWS);
        UpdateShards(dt);
        UpdateTexts(dt);
        UpdateTheme();

        if (flashAlpha > 0f) flashAlpha -= Time.unscaledDeltaTime * 2.2f;
        if (bannerTimer > 0f) bannerTimer -= dt;

        // music reacts to the game
        float targetVol = paused ? 0.08f : (state == State.Playing ? 0.32f : 0.2f);
        music.volume = Mathf.Lerp(music.volume, targetVol, 5f * Time.unscaledDeltaTime);
        music.pitch = 0.95f + Mathf.Clamp01((currentWS - 9f) / 30f) * 0.35f;
    }

    // ---------- keyboard / mouse / touch ----------
    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            muted = !muted;
            AudioListener.volume = muted ? 0f : 1f;
        }

        if (state == State.Menu)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) StartGame(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) StartGame(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) StartGame(2);
            return;
        }

        if (state == State.GameOver)
        {
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return)) StartGame(mode);
            if (Input.GetKeyDown(KeyCode.Backspace)) GoToMenu();
            return;
        }

        // Playing
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
        {
            paused = !paused;
            Time.timeScale = paused ? 0f : 1f;
        }
        if (paused) return;

        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) MoveLeft();
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) MoveRight();
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Space)) DoJump();
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) DoSlide();

        // swipe (mouse drag or finger). A simple tap = jump.
        if (Input.GetMouseButtonDown(0))
        {
            swipeStart = Input.mousePosition;
            swiping = true;
        }
        if (swiping && Input.GetMouseButton(0))
        {
            Vector2 d = (Vector2)Input.mousePosition - swipeStart;
            if (d.magnitude > Screen.height * 0.06f)
            {
                if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
                {
                    if (d.x > 0) MoveRight(); else MoveLeft();
                }
                else
                {
                    if (d.y > 0) DoJump(); else DoSlide();
                }
                swiping = false;
            }
        }
        if (Input.GetMouseButtonUp(0))
        {
            if (swiping) DoJump();
            swiping = false;
        }
    }

    void MoveLeft()
    {
        if (lane > 0) { lane--; Play(cLane, 0.9f, 0.6f); }
    }

    void MoveRight()
    {
        if (lane < 2) { lane++; Play(cLane, 1.1f, 0.6f); }
    }

    void DoJump()
    {
        if (!grounded) return;
        vy = JumpSpeed;
        grounded = false;
        slideTimer = 0f;
        Play(cJump);
        SpawnShards(new Vector3(px, 0.05f, 0f), new Color(0.6f, 0.7f, 0.8f), 5, 2.5f, 0.15f);
    }

    void DoSlide()
    {
        if (grounded)
        {
            slideTimer = 0.65f;
            Play(cSlide);
        }
        else
        {
            vy = -22f;          // fast fall, then slide when landing
            slideQueued = true;
        }
    }

    // ---------- the menu / game over background runner ----------
    void DemoAI(float dt)
    {
        demoTimer -= dt;
        if (demoTimer <= 0f)
        {
            lane = Random.Range(0, 3);
            demoTimer = Random.Range(1f, 2.2f);
            if (Random.value < 0.4f && grounded)
            {
                vy = JumpSpeed;
                grounded = false;
            }
        }
    }

    // ---------- player physics + animation ----------
    void UpdatePlayer(float dt)
    {
        if (slideTimer > 0f) slideTimer -= dt;

        if (!grounded)
        {
            vy -= Gravity * dt;
            py += vy * dt;
            if (py <= 0f)
            {
                py = 0f;
                vy = 0f;
                grounded = true;
                if (state == State.Playing)
                    SpawnShards(new Vector3(px, 0.05f, 0f), new Color(0.6f, 0.7f, 0.8f), 6, 2.5f, 0.15f);
                if (slideQueued)
                {
                    slideQueued = false;
                    slideTimer = 0.65f;
                    Play(cSlide);
                }
            }
        }

        px = Mathf.Lerp(px, LaneX(lane), 1f - Mathf.Exp(-16f * dt));

        // visuals
        player.transform.position = new Vector3(px, py, 0f);
        player.transform.rotation = Quaternion.Euler(0f, 0f, (LaneX(lane) - px) * 8f);

        Vector3 sc = slideTimer > 0f ? new Vector3(1.1f, 0.45f, 1.2f) : Vector3.one;
        player.transform.localScale = Vector3.Lerp(player.transform.localScale, sc, 1f - Mathf.Exp(-25f * dt));

        runPhase += dt * currentWS * 0.9f;
        float sw = (grounded && slideTimer <= 0f) ? Mathf.Sin(runPhase) * 0.25f : 0f;
        legL.localPosition = new Vector3(-0.2f, 0.3f, sw);
        legR.localPosition = new Vector3(0.2f, 0.3f, -sw);

        // blink while invincible
        bool vis = true;
        if (state == State.Playing && invincible > 0f)
            vis = ((int)(Time.time * 14f) % 2 == 0);
        foreach (Renderer r in playerRenderers) r.enabled = vis;

        // power-up rings
        bool playing = state == State.Playing;
        shieldRing.SetActive(playing && shield);
        bool magnetVisible = playing && magnetTimer > 0f && (magnetTimer > 2f || ((int)(Time.time * 8f) % 2 == 0));
        magnetRing.SetActive(magnetVisible);
        Vector3 rp = new Vector3(px, py + 0.9f, 0f);
        shieldRing.transform.position = rp;
        magnetRing.transform.position = rp;
        shieldRing.transform.Rotate(0f, 220f * dt, 0f);
        magnetRing.transform.Rotate(0f, -160f * dt, 0f);
    }

    // =================================================================
    //  GAMEPLAY
    // =================================================================
    void GameplayUpdate(float dt)
    {
        invincible = Mathf.Max(0f, invincible - dt);
        magnetTimer = Mathf.Max(0f, magnetTimer - dt);
        doubleTimer = Mathf.Max(0f, doubleTimer - dt);
        slowFactor = Mathf.MoveTowards(slowFactor, 1f, 0.4f * dt);

        float target = Mathf.Min(modeMax[mode], modeBase[mode] + distance * 0.012f);
        speed = Mathf.MoveTowards(speed, target, 8f * dt);

        float ws = speed * slowFactor;
        currentWS = ws;
        distance += ws * dt;

        // level up
        int lv = 1 + (int)(distance / 350f);
        if (lv > level)
        {
            level = lv;
            bannerTimer = 2.4f;
            bonus += 100;
            Play(cLevel);
            AddText("+100 LEVEL BONUS", new Vector3(px, 2.5f, 0f), Color.yellow);
        }

        // spawn new rows of obstacles
        spawnAcc += ws * dt;
        if (spawnAcc >= nextGap)
        {
            spawnAcc -= nextGap;
            SpawnRow();
            nextGap = Mathf.Max(ws * 0.7f, Random.Range(13f, 19f)) + pendingExtra;
            pendingExtra = 0f;
        }

        UpdateThings(dt, ws);
    }

    void UpdateThings(float dt, float ws)
    {
        float curH = CurHeight();

        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t.go == null) { things.RemoveAt(i); continue; }

            Vector3 p = t.go.transform.position;
            p.z -= ws * dt;

            // sideways movers
            if (t.vx != 0f)
            {
                p.x += t.vx * dt;
                if (p.x > 2.6f) { p.x = 2.6f; t.vx = -Mathf.Abs(t.vx); }
                if (p.x < -2.6f) { p.x = -2.6f; t.vx = Mathf.Abs(t.vx); }
            }

            if (!t.obstacle)
            {
                // magnet pulls coins toward you
                if (magnetTimer > 0f && t.kind == K_COIN && p.z > -1f && p.z < 10f && Mathf.Abs(p.x - px) < 5f)
                    p = Vector3.MoveTowards(p, new Vector3(px, py + 0.8f, 0f), 24f * dt);

                t.go.transform.Rotate(0f, t.spin * dt, 0f, Space.World);
                if (t.kind != K_COIN)
                    t.go.transform.localScale = t.baseScale * (1f + 0.12f * Mathf.Sin(Time.time * 6f));
            }

            t.go.transform.position = p;

            if (p.z < -6f)
            {
                Destroy(t.go);
                things.RemoveAt(i);
                continue;
            }

            if (t.obstacle)
            {
                if (invincible > 0f) continue;
                if (Mathf.Abs(p.z) < t.halfD + 0.4f &&
                    Mathf.Abs(p.x - px) < t.halfW + 0.3f &&
                    t.y1 > py + 0.08f && t.y0 < py + curH - 0.08f)
                {
                    Crash(t, p);
                    Destroy(t.go);
                    things.RemoveAt(i);
                }
            }
            else
            {
                if (Mathf.Abs(p.z) < 0.9f && Mathf.Abs(p.x - px) < 0.95f &&
                    p.y + 0.3f > py && p.y - 0.3f < py + curH)
                {
                    Collect(t, p);
                    Destroy(t.go);
                    things.RemoveAt(i);
                }
            }
        }
    }

    void Crash(Thing t, Vector3 pos)
    {
        Color c = t.kind == K_LOW ? new Color(1f, 0.3f, 0.2f) :
                  t.kind == K_HIGH ? new Color(1f, 0.2f, 0.7f) : new Color(0.4f, 0.4f, 1f);
        SpawnShards(pos + Vector3.up * 0.8f, c, 20, 7f, 0.3f);

        if (shield)
        {
            shield = false;
            Play(cShield);
            AddText("SHIELD BROKE!", new Vector3(px, 2.4f, 0f), new Color(0.3f, 0.95f, 1f));
            shakeTime = 0.2f;
            return;
        }

        lives--;
        coinStreak = 0;
        invincible = 1.8f;
        slowFactor = 0.55f;
        shakeTime = 0.35f;
        flashAlpha = 0.5f;
        flashColor = Color.red;
        Play(cHit);
        AddText("OUCH!", new Vector3(px, 2.4f, 0f), Color.red);

        if (lives <= 0) pendingEnd = true;
    }

    void Collect(Thing t, Vector3 pos)
    {
        Vector3 tp = new Vector3(pos.x, pos.y + 0.5f, 0f);

        if (t.kind == K_COIN)
        {
            coins++;
            coinStreak++;
            int val = 10 * Mult() * (doubleTimer > 0f ? 2 : 1);
            bonus += val;
            Play(cCoin, 1f + Mathf.Min(coinStreak, 15) * 0.04f, 0.8f);
            SpawnShards(pos, new Color(1f, 0.9f, 0.2f), 6, 3.5f, 0.12f);
            AddText("+" + val, tp, new Color(1f, 0.95f, 0.4f));
        }
        else if (t.kind == K_SHIELD)
        {
            shield = true;
            Play(cPower);
            SpawnShards(pos, new Color(0.2f, 0.9f, 1f), 14, 5f, 0.18f);
            AddText("SHIELD!", tp, new Color(0.3f, 0.95f, 1f));
        }
        else if (t.kind == K_MAGNET)
        {
            magnetTimer = 8f;
            Play(cPower);
            SpawnShards(pos, new Color(1f, 0.55f, 0.1f), 14, 5f, 0.18f);
            AddText("MAGNET!", tp, new Color(1f, 0.6f, 0.2f));
        }
        else if (t.kind == K_DOUBLE)
        {
            doubleTimer = 10f;
            Play(cPower);
            SpawnShards(pos, new Color(0.2f, 1f, 0.3f), 14, 5f, 0.18f);
            AddText("DOUBLE POINTS!", tp, new Color(0.3f, 1f, 0.4f));
        }
        else if (t.kind == K_HEART)
        {
            if (lives < maxLives)
            {
                lives++;
                AddText("+1 LIFE", tp, new Color(1f, 0.5f, 0.6f));
            }
            else
            {
                bonus += 50;
                AddText("+50", tp, new Color(1f, 0.5f, 0.6f));
            }
            Play(cHeart);
            SpawnShards(pos, new Color(1f, 0.5f, 0.6f), 14, 5f, 0.18f);
        }
    }

    // =================================================================
    //  SPAWNING
    // =================================================================
    Thing MakeObstacle(int kind, float x, float z)
    {
        Thing t = new Thing();
        t.kind = kind;
        t.obstacle = true;
        t.halfW = 0.9f;
        t.halfD = 0.55f;

        Vector3 scale;
        float cy;
        Material m;

        if (kind == K_LOW)          // jump over it
        {
            t.y0 = 0f; t.y1 = 0.9f;
            scale = new Vector3(1.8f, 0.9f, 1.1f);
            cy = 0.45f; m = matLow;
        }
        else if (kind == K_HIGH)    // slide under it
        {
            t.y0 = 0.95f; t.y1 = 2.0f;
            scale = new Vector3(1.8f, 1.05f, 1.1f);
            cy = 1.475f; m = matHigh;
        }
        else                        // tall wall: change lane
        {
            t.y0 = 0f; t.y1 = 3.2f;
            scale = new Vector3(1.8f, 3.2f, 1.1f);
            cy = 1.6f; m = matTall;
        }

        t.go = Prim(PrimitiveType.Cube, new Vector3(x, cy, z), scale, m);

        // bright stripes on the front so obstacles are easy to read
        float sy = 0.12f / scale.y;
        if (kind == K_TALL)
        {
            Prim(PrimitiveType.Cube, new Vector3(0f, 0.3f, -0.52f), new Vector3(0.9f, sy, 0.04f), matStripe, t.go.transform);
            Prim(PrimitiveType.Cube, new Vector3(0f, -0.1f, -0.52f), new Vector3(0.9f, sy, 0.04f), matStripe, t.go.transform);
            Prim(PrimitiveType.Cube, new Vector3(0f, -0.3f, -0.52f), new Vector3(0.9f, sy, 0.04f), matStripe, t.go.transform);
        }
        else
        {
            Prim(PrimitiveType.Cube, new Vector3(0f, 0f, -0.52f), new Vector3(0.9f, sy, 0.04f), matStripe, t.go.transform);
        }

        things.Add(t);
        return t;
    }

    Thing MakePickup(int kind, float x, float y, float z)
    {
        Thing t = new Thing();
        t.kind = kind;
        t.obstacle = false;
        t.halfW = 0.5f;
        t.halfD = 0.5f;
        t.spin = 200f;

        Material m;
        Vector3 scale;
        if (kind == K_COIN) { m = matCoin; scale = new Vector3(0.55f, 0.55f, 0.12f); }
        else
        {
            scale = Vector3.one * 0.8f;
            if (kind == K_SHIELD) m = matShield;
            else if (kind == K_MAGNET) m = matMagnet;
            else if (kind == K_DOUBLE) m = matDouble;
            else m = matHeart;
        }

        t.baseScale = scale;
        t.go = Prim(PrimitiveType.Sphere, new Vector3(x, y, z), scale, m);
        things.Add(t);
        return t;
    }

    void CoinLine(float x, float z0, int n, float y)
    {
        for (int i = 0; i < n; i++)
            MakePickup(K_COIN, x, y, z0 + i * 1.6f);
    }

    void CoinArc(float x, float zc)
    {
        for (int i = 0; i < 5; i++)
        {
            float u = (i - 2) / 2f;
            float y = 0.7f + 1.0f * (1f - u * u);
            MakePickup(K_COIN, x, y, zc + (i - 2) * 1.6f);
        }
    }

    void SpawnRow()
    {
        float z = SpawnZ;
        float r = Random.value;
        float slalomP = Mathf.Min(0.06f + 0.03f * level, 0.25f);
        float comboP = level >= 2 ? 0.12f : 0f;

        if (r < slalomP) PatternSlalom(z);
        else if (r < slalomP + comboP) PatternCombo(z);
        else if (r < slalomP + comboP + 0.16f) PatternFull(z, K_LOW);
        else if (r < slalomP + comboP + 0.30f) PatternFull(z, K_HIGH);
        else PatternLanes(z);
    }

    // a full-width barrier: you must jump (LOW) or slide (HIGH)
    void PatternFull(float z, int kind)
    {
        for (int l = 0; l < 3; l++) MakeObstacle(kind, LaneX(l), z);
        if (kind == K_LOW) CoinArc(0f, z);
        else CoinLine(0f, z - 3f, 4, 0.5f);
    }

    // one safe lane, other lanes blocked
    void PatternLanes(float z)
    {
        int safe = Random.Range(0, 3);
        for (int l = 0; l < 3; l++)
        {
            if (l == safe) continue;
            if (Random.value < 0.18f) continue;     // sometimes leave a lane open

            float rr = Random.value;
            int k = rr < 0.4f ? K_LOW : (rr < 0.72f ? K_HIGH : K_TALL);
            Thing t = MakeObstacle(k, LaneX(l), z);
            if (level >= 3 && k == K_LOW && Random.value < 0.35f)
                t.vx = (Random.value < 0.5f ? -1f : 1f) * 2.5f;
        }

        CoinLine(LaneX(safe), z - 4f, 5, 0.6f);

        if (Random.value < 0.12f)
        {
            int pk = PickPowerup();
            MakePickup(pk, LaneX(safe), 0.9f, z + 6f);
        }
    }

    int PickPowerup()
    {
        float r = Random.value;
        if (r < 0.30f) return K_SHIELD;
        if (r < 0.60f) return K_MAGNET;
        if (r < 0.85f) return K_DOUBLE;
        return lives < maxLives ? K_HEART : K_SHIELD;
    }

    // three tall walls in a row: weave left and right
    void PatternSlalom(float z)
    {
        int[][] seqs =
        {
            new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, new[] { 1, 0, 1 },
            new[] { 1, 2, 1 }, new[] { 0, 1, 0 }, new[] { 2, 1, 2 }
        };
        int[] seq = seqs[Random.Range(0, seqs.Length)];

        for (int k = 0; k < 3; k++)
        {
            float zk = z + k * 12f;
            for (int l = 0; l < 3; l++)
                if (l != seq[k]) MakeObstacle(K_TALL, LaneX(l), zk);
            CoinLine(LaneX(seq[k]), zk - 1.6f, 3, 0.6f);
        }
        pendingExtra = 26f;
    }

    // a jump immediately followed by a slide
    void PatternCombo(float z)
    {
        for (int l = 0; l < 3; l++) MakeObstacle(K_LOW, LaneX(l), z);
        for (int l = 0; l < 3; l++) MakeObstacle(K_HIGH, LaneX(l), z + 15f);
        CoinArc(0f, z);
        CoinLine(0f, z + 12f, 4, 0.5f);
        pendingExtra = 18f;
    }

    // =================================================================
    //  WORLD SCROLLING + THEME
    // =================================================================
    void UpdateWorld(float dt, float ws)
    {
        foreach (Transform t in crossLines)
        {
            Vector3 p = t.position;
            p.z -= ws * dt;
            if (p.z < -8f) p.z += 128f;
            t.position = p;
        }

        foreach (Transform t in pillars)
        {
            Vector3 p = t.position;
            p.z -= ws * dt;
            if (p.z < -12f) p.z += 132f;
            t.position = p;
        }

        bool showStreaks = ws > 16f;
        foreach (Transform t in streaks)
        {
            if (t.gameObject.activeSelf != showStreaks) t.gameObject.SetActive(showStreaks);
            if (!showStreaks) continue;
            Vector3 p = t.position;
            p.z -= ws * dt * 1.8f;
            if (p.z < -6f)
            {
                p = new Vector3(Random.Range(-12f, 12f), Random.Range(0.3f, 8f), 60f + Random.Range(0f, 10f));
            }
            t.position = p;
        }
    }

    void UpdateTheme()
    {
        int idx = (level - 1) % themeCol.Length;
        float k = 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime);
        curCol = Color.Lerp(curCol, themeCol[idx], k);
        curBg = Color.Lerp(curBg, themeBg[idx], k);

        matGrid.color = curCol;
        matPillar.color = Dark(curCol, 0.35f);
        matFloor.color = Dark(curCol, 0.06f);
        cam.backgroundColor = curBg;
        RenderSettings.fogColor = curBg;
    }

    // =================================================================
    //  CAMERA
    // =================================================================
    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        Vector3 target = new Vector3(px * 0.4f, 4.3f, -7.5f);
        camPos = Vector3.Lerp(camPos, target, 1f - Mathf.Exp(-8f * dt));

        Vector3 off = Vector3.zero;
        if (shakeTime > 0f)
        {
            shakeTime -= dt;
            off = Random.insideUnitSphere * 0.25f;
        }

        cam.transform.position = camPos + off;
        cam.transform.LookAt(new Vector3(px * 0.25f, 1.3f, 9f));

        float tf = 62f + Mathf.Clamp01((currentWS - 10f) / 30f) * 16f;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, tf, 1f - Mathf.Exp(-3f * dt));
    }

    // =================================================================
    //  EFFECTS
    // =================================================================
    void SpawnShards(Vector3 pos, Color c, int count, float spd, float size)
    {
        if (shards.Count > 220) return;
        Material m = ColMat(c);
        for (int i = 0; i < count; i++)
        {
            GameObject g = Prim(PrimitiveType.Cube, pos, Vector3.one * size, m);
            g.transform.rotation = Random.rotation;

            Shard s = new Shard();
            s.go = g;
            s.vel = Random.insideUnitSphere * spd;
            s.life = 0.7f;
            s.maxLife = 0.7f;
            s.size = size;
            shards.Add(s);
        }
    }

    void UpdateShards(float dt)
    {
        for (int i = shards.Count - 1; i >= 0; i--)
        {
            Shard s = shards[i];
            s.life -= dt;
            if (s.life <= 0f || s.go == null)
            {
                if (s.go != null) Destroy(s.go);
                shards.RemoveAt(i);
                continue;
            }
            s.vel.y -= 10f * dt;
            s.go.transform.position += s.vel * dt;
            s.go.transform.localScale = Vector3.one * s.size * (s.life / s.maxLife);
        }
    }

    void AddText(string t, Vector3 pos, Color c)
    {
        FloatText f = new FloatText();
        f.text = t;
        f.pos = pos;
        f.color = c;
        f.life = 0.9f;
        texts.Add(f);
    }

    void UpdateTexts(float dt)
    {
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            texts[i].life -= dt;
            texts[i].pos += Vector3.up * 1.4f * dt;
            if (texts[i].life <= 0f) texts.RemoveAt(i);
        }
    }

    // =================================================================
    //  BLOCKY PIXEL FONT (for the big titles)
    // =================================================================
    static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
    {
        {'A', new[]{"01110","10001","10001","11111","10001","10001","10001"}},
        {'D', new[]{"11110","10001","10001","10001","10001","10001","11110"}},
        {'E', new[]{"11111","10000","10000","11110","10000","10000","11111"}},
        {'G', new[]{"01110","10001","10000","10111","10001","10001","01110"}},
        {'H', new[]{"10001","10001","10001","11111","10001","10001","10001"}},
        {'L', new[]{"10000","10000","10000","10000","10000","10000","11111"}},
        {'M', new[]{"10001","11011","10101","10101","10001","10001","10001"}},
        {'N', new[]{"10001","11001","10101","10101","10011","10001","10001"}},
        {'O', new[]{"01110","10001","10001","10001","10001","10001","01110"}},
        {'P', new[]{"11110","10001","10001","11110","10000","10000","10000"}},
        {'R', new[]{"11110","10001","10001","11110","10100","10010","10001"}},
        {'S', new[]{"01111","10000","10000","01110","00001","00001","11110"}},
        {'U', new[]{"10001","10001","10001","10001","10001","10001","01110"}},
        {'V', new[]{"10001","10001","10001","10001","10001","01010","00100"}},
        {'0', new[]{"01110","10001","10011","10101","11001","10001","01110"}},
        {'1', new[]{"00100","01100","00100","00100","00100","00100","01110"}},
        {'2', new[]{"01110","10001","00001","00010","00100","01000","11111"}},
        {'3', new[]{"11110","00001","00001","01110","00001","00001","11110"}},
        {'4', new[]{"00010","00110","01010","10010","11111","00010","00010"}},
        {'5', new[]{"11111","10000","11110","00001","00001","10001","01110"}},
        {'6', new[]{"00110","01000","10000","11110","10001","10001","01110"}},
        {'7', new[]{"11111","00001","00010","00100","01000","01000","01000"}},
        {'8', new[]{"01110","10001","10001","01110","10001","10001","01110"}},
        {'9', new[]{"01110","10001","10001","01111","00001","00010","01100"}},
        {' ', new[]{"00000","00000","00000","00000","00000","00000","00000"}},
    };

    void DrawPixelText(string str, Rect area, Color color)
    {
        int n = str.Length;
        float cols = n * 6 - 1;
        float cell = Mathf.Min(area.width / cols, area.height / 7f);
        float startX = area.x + (area.width - cols * cell) / 2f;
        float startY = area.y + (area.height - 7f * cell) / 2f;
        float gap = cell * 0.08f;

        Color old = GUI.color;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                if (!Font.ContainsKey(str[i])) continue;
                string[] rows = Font[str[i]];
                for (int r = 0; r < 7; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        if (rows[r][c] != '1') continue;
                        float x = startX + (i * 6 + c) * cell;
                        float y = startY + r * cell;

                        if (pass == 0)
                        {
                            GUI.color = new Color(0f, 0f, 0f, 0.6f * color.a);
                            GUI.DrawTexture(new Rect(x + cell * 0.18f, y + cell * 0.18f, cell - gap, cell - gap), Texture2D.whiteTexture);
                        }
                        else
                        {
                            GUI.color = color;
                            GUI.DrawTexture(new Rect(x, y, cell - gap, cell - gap), Texture2D.whiteTexture);
                            GUI.color = new Color(1f, 1f, 1f, 0.35f * color.a);
                            GUI.DrawTexture(new Rect(x, y, (cell - gap) * 0.45f, (cell - gap) * 0.45f), Texture2D.whiteTexture);
                        }
                    }
                }
            }
        }
        GUI.color = old;
    }

    // =================================================================
    //  GUI HELPERS
    // =================================================================
    GUIStyle MakeStyle(int size, TextAnchor a, bool bold)
    {
        GUIStyle s = new GUIStyle(GUI.skin.label);
        s.fontSize = size;
        s.alignment = a;
        s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        s.wordWrap = false;
        return s;
    }

    void Label(Rect r, string t, GUIStyle s, Color c)
    {
        s.normal.textColor = new Color(0f, 0f, 0f, 0.75f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, s);
        s.normal.textColor = c;
        GUI.Label(r, t, s);
    }

    void Box(Rect r, Color c)
    {
        Color o = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = o;
    }

    void DrawHeart(float x, float y, float size, Color c)
    {
        string[] rows = { "01010", "11111", "11111", "01110", "00100" };
        float cell = size / 5f;
        for (int r = 0; r < 5; r++)
            for (int col = 0; col < 5; col++)
                if (rows[r][col] == '1')
                    Box(new Rect(x + col * cell, y + r * cell, cell - 1f, cell - 1f), c);
    }

    void PowerBar(float x, float y, float w, float h, float frac, Color c)
    {
        Box(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.5f));
        Box(new Rect(x, y, w * Mathf.Clamp01(frac), h), c);
    }

    // =================================================================
    //  ON-SCREEN UI
    // =================================================================
    void OnGUI()
    {
        int W = Screen.width, H = Screen.height;
        int mid = H / 22, small = H / 38;

        GUIStyle sMidL = MakeStyle(mid, TextAnchor.MiddleLeft, true);
        GUIStyle sMidR = MakeStyle(mid, TextAnchor.MiddleRight, true);
        GUIStyle sMidC = MakeStyle(mid, TextAnchor.MiddleCenter, true);
        GUIStyle sSmallL = MakeStyle(small, TextAnchor.MiddleLeft, false);
        GUIStyle sSmallR = MakeStyle(small, TextAnchor.MiddleRight, false);
        GUIStyle sSmallC = MakeStyle(small, TextAnchor.MiddleCenter, false);

        GUIStyle btn = new GUIStyle(GUI.skin.button);
        btn.fontSize = (int)(small * 1.3f);
        btn.fontStyle = FontStyle.Bold;

        if (flashAlpha > 0f)
            Box(new Rect(0, 0, W, H), new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha));

        if (state == State.Playing)
        {
            DrawHUD(W, H, mid, small, sMidL, sMidR, sMidC, sSmallL, sSmallR, sSmallC);
            return;
        }

        // ---------- menu / game over panel ----------
        Rect panel = new Rect(W * 0.2f, H * 0.05f, W * 0.6f, H * 0.9f);
        Box(panel, new Color(0.02f, 0.03f, 0.08f, 0.85f));
        Box(new Rect(panel.x, panel.y, panel.width, 3f), curCol);
        Box(new Rect(panel.x, panel.yMax - 3f, panel.width, 3f), curCol);

        float cx = panel.x, y = panel.y, w = panel.width, h = panel.height;
        Color rainbow = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.15f, 1f), 0.7f, 1f);

        if (state == State.Menu)
        {
            DrawPixelText("NEON DASH", new Rect(cx + w * 0.1f, y + h * 0.02f, w * 0.8f, h * 0.13f), rainbow);
            Label(new Rect(cx, y + h * 0.155f, w, h * 0.045f), "Run. Jump. Slide. Don't crash!", sSmallC, new Color(0.85f, 0.9f, 1f));

            string[] ctrl =
            {
                "A / D or Arrow keys  =  change lane",
                "W / Up / Space  =  jump          S / Down  =  slide",
                "Touch screen:  swipe left, right, up or down",
                "P / Esc  =  pause          M  =  mute"
            };
            for (int i = 0; i < ctrl.Length; i++)
                Label(new Rect(cx, y + h * (0.215f + i * 0.042f), w, h * 0.042f), ctrl[i], sSmallC, Color.white);

            string[] legTxt =
            {
                "Low red block:  JUMP over it",
                "High pink bar:  SLIDE under it",
                "Tall blue wall:  switch lane",
                "Coin:  points (streaks raise your multiplier)",
                "Shield:  survives one crash",
                "Magnet:  pulls in coins (8s)",
                "Double:  x2 points (10s)",
                "Heart:  +1 life"
            };
            Color[] legCol =
            {
                new Color(1f, 0.25f, 0.2f), new Color(1f, 0.2f, 0.7f), new Color(0.4f, 0.4f, 1f),
                new Color(1f, 0.85f, 0.1f), new Color(0.2f, 0.9f, 1f), new Color(1f, 0.55f, 0.1f),
                new Color(0.2f, 1f, 0.3f), new Color(1f, 0.5f, 0.6f)
            };
            for (int i = 0; i < legTxt.Length; i++)
            {
                float ly = y + h * (0.405f + i * 0.04f);
                Box(new Rect(cx + w * 0.16f, ly + h * 0.01f, h * 0.022f, h * 0.022f), legCol[i]);
                Label(new Rect(cx + w * 0.21f, ly, w * 0.7f, h * 0.04f), legTxt[i], sSmallL, Color.white);
            }

            Label(new Rect(cx, y + h * 0.735f, w, h * 0.045f), "CHOOSE A MODE  (or press 1 / 2 / 3)", sSmallC, rainbow);

            float bw = w * 0.28f;
            for (int i = 0; i < 3; i++)
            {
                float bx = cx + w * 0.04f + i * (bw + w * 0.04f);
                string label = modeName[i] + "\n" + modeLives[i] + " lives\nBest: " + PlayerPrefs.GetInt("NDBest" + i, 0);
                if (GUI.Button(new Rect(bx, y + h * 0.79f, bw, h * 0.16f), label, btn))
                    StartGame(i);
            }
        }
        else // GameOver
        {
            DrawPixelText("GAME OVER", new Rect(cx + w * 0.08f, y + h * 0.04f, w * 0.84f, h * 0.15f), new Color(1f, 0.3f, 0.3f));

            Label(new Rect(cx, y + h * 0.22f, w, h * 0.06f), "Mode: " + modeName[mode], sSmallC, Color.white);
            Label(new Rect(cx, y + h * 0.29f, w, h * 0.08f), "Score: " + Score, sMidC, Color.white);
            Label(new Rect(cx, y + h * 0.38f, w, h * 0.08f), "Best Score: " + bestScore, sMidC, new Color(1f, 0.9f, 0.4f));
            Label(new Rect(cx, y + h * 0.47f, w, h * 0.06f), "Distance: " + Mathf.FloorToInt(distance) + " m", sSmallC, Color.white);
            Label(new Rect(cx, y + h * 0.53f, w, h * 0.06f), "Coins: " + coins, sSmallC, Color.white);
            Label(new Rect(cx, y + h * 0.59f, w, h * 0.06f), "Level reached: " + level, sSmallC, Color.white);

            if (newBest && ((int)(Time.unscaledTime * 3f) % 2 == 0))
                Label(new Rect(cx, y + h * 0.67f, w, h * 0.08f), "NEW BEST!", sMidC, Color.yellow);

            if (GUI.Button(new Rect(cx + w * 0.05f, y + h * 0.80f, w * 0.43f, h * 0.14f), "PLAY AGAIN  (R)", btn))
                StartGame(mode);
            if (GUI.Button(new Rect(cx + w * 0.52f, y + h * 0.80f, w * 0.43f, h * 0.14f), "MENU  (Backspace)", btn))
                GoToMenu();
        }
    }

    void DrawHUD(int W, int H, int mid, int small,
                 GUIStyle sMidL, GUIStyle sMidR, GUIStyle sMidC,
                 GUIStyle sSmallL, GUIStyle sSmallR, GUIStyle sSmallC)
    {
        // floating texts
        foreach (FloatText f in texts)
        {
            Vector3 sp = cam.WorldToScreenPoint(f.pos);
            if (sp.z <= 0f) continue;
            Color c = new Color(f.color.r, f.color.g, f.color.b, Mathf.Clamp01(f.life * 2f));
            Label(new Rect(sp.x - 200, H - sp.y - mid, 400, mid * 2), f.text, sMidC, c);
        }

        // score block (top-left)
        Label(new Rect(20, 10, 700, mid * 1.4f), "SCORE " + Score, sMidL, Color.white);
        Label(new Rect(20, 10 + mid * 1.3f, 700, small * 1.6f),
              "COINS " + coins + "     " + Mathf.FloorToInt(distance) + " m", sSmallL, new Color(0.85f, 0.9f, 1f));

        // lives (top-center)
        float hs = mid * 0.8f;
        float totalW = maxLives * hs * 1.3f;
        for (int i = 0; i < maxLives; i++)
        {
            Color hc = i < lives ? new Color(1f, 0.25f, 0.35f) : new Color(0.25f, 0.25f, 0.3f);
            DrawHeart(W / 2f - totalW / 2f + i * hs * 1.3f, 14f, hs, hc);
        }

        // level + mode (top-right)
        Label(new Rect(W - 420, 10, 400, mid * 1.4f), "LEVEL " + level, sMidR, curCol);
        Label(new Rect(W - 420, 10 + mid * 1.3f, 400, small * 1.6f),
              modeName[mode] + "   Best " + bestScore, sSmallR, new Color(0.85f, 0.9f, 1f));
        Label(new Rect(W - 420, 10 + mid * 1.3f + small * 1.6f, 400, small * 1.6f),
              "SPEED " + Mathf.RoundToInt(speed * slowFactor * 3.6f) + " km/h", sSmallR, new Color(0.85f, 0.9f, 1f));

        // active power-ups (left side)
        float py0 = 10 + mid * 1.3f + small * 2.2f;
        float line = small * 1.8f;

        if (coinStreak >= 5)
        {
            Label(new Rect(20, py0, 600, small * 1.6f), "COIN COMBO x" + Mult() + "  (" + coinStreak + ")", sSmallL, Color.yellow);
            py0 += line;
        }
        if (shield)
        {
            Label(new Rect(20, py0, 600, small * 1.6f), "SHIELD READY", sSmallL, new Color(0.3f, 0.95f, 1f));
            py0 += line;
        }
        if (magnetTimer > 0f)
        {
            Label(new Rect(20, py0, 200, small * 1.6f), "MAGNET", sSmallL, new Color(1f, 0.6f, 0.2f));
            PowerBar(150, py0 + small * 0.5f, small * 7f, small * 0.6f, magnetTimer / 8f, new Color(1f, 0.55f, 0.1f));
            py0 += line;
        }
        if (doubleTimer > 0f)
        {
            Label(new Rect(20, py0, 200, small * 1.6f), "x2 SCORE", sSmallL, new Color(0.3f, 1f, 0.4f));
            PowerBar(150, py0 + small * 0.5f, small * 7f, small * 0.6f, doubleTimer / 10f, new Color(0.2f, 1f, 0.3f));
            py0 += line;
        }

        // tutorial hint at the start of a run
        if (distance < 70f)
            Label(new Rect(0, H - small * 3f, W, small * 2f),
                  "A / D = lanes     W / Space = jump     S = slide     (or swipe)", sSmallC, new Color(1f, 1f, 1f, 0.9f));

        // level banner
        if (bannerTimer > 0f)
        {
            float a = Mathf.Clamp01(bannerTimer);
            DrawPixelText("LEVEL " + level, new Rect(W * 0.25f, H * 0.25f, W * 0.5f, H * 0.14f),
                          new Color(curCol.r, curCol.g, curCol.b, a));
        }

        // pause screen
        if (paused)
        {
            Box(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.65f));
            DrawPixelText("PAUSED", new Rect(W * 0.3f, H * 0.25f, W * 0.4f, H * 0.15f), Color.white);
            Label(new Rect(0, H * 0.5f, W, mid * 1.5f), "Press P or Esc to continue", sMidC, Color.white);
        }
        else
        {
            Label(new Rect(W - 420, H - small * 2.5f, 400, small * 2f), "P = pause   M = mute", sSmallR, new Color(1f, 1f, 1f, 0.6f));
        }
    }
}
