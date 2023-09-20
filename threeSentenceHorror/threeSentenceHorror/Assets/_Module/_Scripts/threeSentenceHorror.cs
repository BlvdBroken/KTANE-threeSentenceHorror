using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.PostProcessing;
using KModkit;
using Wawa.Optionals;
using Wawa.DDL;
using Wawa.IO;
using Rand = UnityEngine.Random;

public class threeSentenceHorror : MonoBehaviour {

    public KMBombModule Module;
    public KMBombInfo BombInfo;
    private KMBomb Bomb;
    public KMAudio Audio;
    public KMBossModule BossModule;
    private FloatingHoldable HeldBomb;

    public KMSelectable Keyway;
    public GameObject KeyCollectSub;
    public GameObject KeyGet;
    public PostProcessVolume PostProcess;
    public PostProcessResources postProcessResources;
    private bool _hasKey = false;
    public static bool _isPlaying = false;
    public static int _isSpooking = 0;
    private int _activations = 0;
    public static float MicLoudness;
    private int _sampleWindow = 128;
    private bool _isInitialized;
    private string _device;
    private bool micless = false;
    private float _prevRate;
    private bool jumpscareMode;

    private AudioClip _clipRecord = new AudioClip();
    public AudioClip ambience;
    private KMAudio.KMAudioRef ambienceRef;
    public AudioClip breathing;
    private KMAudio.KMAudioRef breathingRef;
    public AudioClip walking;
    private KMAudio.KMAudioRef walkingRef;
    public AudioClip heartbeat;
    private KMAudio.KMAudioRef heartbeatRef;

    private IDictionary<string, object> tpAPI;

    static int ModuleIdCounter = 1;
    int _moduleId;
    private bool _moduleSolved;
    private string[] ignored;

    void Awake ()
    {
        _moduleId = ModuleIdCounter++;
        Keyway.OnInteract += delegate () { StartCoroutine(Unlock(_hasKey)); return false; };
    }

    void OnDestroy () { //Shit you need to do when the bomb ends
        _isPlaying = false;
        _isSpooking = 0;
        KeyCollectSub.GetComponent<Text>().text = "";
        StopMicrophone();
        if (breathingRef != null) breathingRef.StopSound();
        if (walkingRef != null) walkingRef.StopSound();
        if (heartbeatRef != null) heartbeatRef.StopSound();
        if (ambienceRef != null) ambienceRef.StopSound();
        KeyGet.SetActive(false);
        PostProcess.gameObject.SetActive(false);
    }

    void Activate () { //Shit that should happen when the bomb arrives (factory)/Lights turn on
        Bomb = Module.GetComponentInParent<KMBomb>();
        InitMic();
        _isInitialized = true;
        Wawa.DDL.Preferences.Music = 0;
        var ModSettings = new Config<TSHSettings>();
        if (Wawa.DDL.Missions.Description.Contains("It seems you are not alone.") || ModSettings.Read().IncludeJumpscares)
        {
            jumpscareMode = true;
        }
        PostProcessLayer VigLayer = Camera.main.gameObject.AddComponent<PostProcessLayer>();
        VigLayer.Init(postProcessResources);
        VigLayer.volumeLayer = LayerMask.GetMask("Post Processing");
        PostProcess.gameObject.layer = LayerMask.NameToLayer("Post Processing");
        PostProcess.gameObject.SetActive(true);
        GameObject tpAPIGameObject = GameObject.Find("TwitchPlays_Info");
        if (tpAPIGameObject != null)
            tpAPI = tpAPIGameObject.GetComponent<IDictionary<string, object>>();
        ambienceRef = Audio.PlaySoundAtTransformWithRef("ambience", Module.transform);
        StartCoroutine(waitBetweenSpooks());
    }

    void Start ()
    { //Shit
        GetComponent<KMBombModule>().OnActivate += Activate;
        ignored = ((BossModule.GetIgnoredModules(Module).Length > 0) ? BossModule.GetIgnoredModules(Module) : new string[] {"Three Sentence Horror"});
        HeldBomb = Module.GetComponentInParent<FloatingHoldable>();
        KeyGet.SetActive(false);
    }

    void Update ()
    { //Shit that happens at any point after initialization
        if (!_moduleSolved)
        {
            IEnumerable<string> solvableList = BombInfo.GetSolvableModuleNames().Where(x => !ignored.Contains(x));
		    IEnumerable<string> solvedList = BombInfo.GetSolvedModuleNames().Where(x => solvableList.Contains(x));
            if (BombInfo.GetSolvableModuleNames().Count() == 1 || solvedList.Count() >= solvableList.Count())
            {
                _activations = 100;
            }
            // Why is everything so scuffed
		    /*else if (solvedList.Count() >= solvableList.Count())
            { //if every non-ignored solvable module is solved
                _isSpooking = 3;
                StopCoroutine(Spook());
                StopCoroutine(waitBetweenSpooks());
            }*/
        }
        switch (_isSpooking)
        {
            case 0:
                break;
            case 1:
                // levelMax equals to the highest normalized value power 2, a small number because < 1
		        // pass the value to a static var so we can access it from anywhere
		        MicLoudness = LevelMax() * 100;
                if (MicLoudness > 8)
                {
                    Strike();
                    DebugMsg("They heard you...");
                }
                break;
            case 2:
                if (HeldBomb.HoldState == FloatingHoldable.HoldStateEnum.Held)
                {
                    Strike();
                    DebugMsg("The flashing lights seem to attract them...");
                }
                break;
            case 3:
                _isSpooking = 0;
                Ending();
                break;
        }
    }

    void Solve ()
    {
        KeyCollectSub.GetComponent<Text>().text = "";
        _moduleSolved = true;
        Wawa.DDL.KMBombStrikeExtensions.SetRate(Bomb, _prevRate);
        if (breathingRef != null) breathingRef.StopSound();
        if (walkingRef != null) walkingRef.StopSound();
        if (heartbeatRef != null) heartbeatRef.StopSound();
        if (ambienceRef != null) ambienceRef.StopSound();
        GetComponent<KMBombModule>().HandlePass();
    }

    void Strike ()
    {
        GetComponent<KMBombModule>().HandleStrike();
        _isSpooking = 0;
        if (breathingRef != null) breathingRef.StopSound();
        if (walkingRef != null) walkingRef.StopSound();
    }

    void Ending()
    {
        _hasKey = true;
        KeyGet.SetActive(true);
        _prevRate = Wawa.DDL.KMBombStrikeExtensions.GetRate(Bomb);
        Wawa.DDL.KMBombStrikeExtensions.SetRate(Bomb, 5 * _prevRate, true);
        heartbeatRef = Audio.PlaySoundAtTransformWithRef("heartbeat", Module.transform);
        KeyCollectSub.GetComponent<Text>().text = "I think someone's coming, I better get out of here.";
    }

    /*private IEnumerator AmbientNoise()
    {
        if (!_moduleSolved)
        {
            
            yield return new WaitForSeconds(30);
        }
    }*/

    //mic initialization
    void InitMic()
    {
        if (Microphone.devices.Length < 1)
        {
            DebugMsg("I Have No Mouth, and I Must Scream");
            micless = true;
        }
        else
        {
            if(_device == null) _device = Microphone.devices[0];
            _clipRecord = Microphone.Start(_device, true, 999, 44100);
        }
    }

    void StopMicrophone()
    {
        Microphone.End(_device);
    }

    //get data from microphone into audioclip
    float LevelMax()
    {
        float levelMax = 0;
        float[] waveData = new float[_sampleWindow];
        int micPosition = Microphone.GetPosition(null)-(_sampleWindow+1); // null means the first microphone
        if (micPosition < 0) return 0;
        _clipRecord.GetData(waveData, micPosition);
        // Getting a peak on the last 128 samples
        for (int i = 0; i < _sampleWindow; i++) {
            float wavePeak = waveData[i] * waveData[i];
            if (levelMax < wavePeak) {
                levelMax = wavePeak;
            }
        }
        return levelMax;
    }


    private IEnumerator waitBetweenSpooks()
    {
        if (breathingRef != null) breathingRef.StopSound();
        if (walkingRef != null) walkingRef.StopSound();
        yield return new WaitForSeconds(Rand.Range(20, 60));
        StartCoroutine(Spook());
        DebugMsg("Spooking");
    }

    private IEnumerator Spook()
    {
        int choosy = Rand.Range(1, 101);
        DebugMsg(choosy.ToString());
        if (choosy < _activations)
        {
            _isSpooking = 3;
            yield return null;
        }
        else if ((choosy % 2 == 0) && (micless == false))
        {
            if (tpAPI != null)
                tpAPI["ircConnectionSendMessage"] = "*FOOTSTEPS*";
            walkingRef = Audio.PlaySoundAtTransformWithRef("walking", Module.transform);
            if (tpAPI != null)
            {
                yield return new WaitForSeconds(8);
                _isSpooking = 1;
                yield return new WaitForSeconds(12);
            }
            else
            {
                yield return new WaitForSeconds(5);
                _isSpooking = 1;
                yield return new WaitForSeconds(15);
            }
            _isSpooking = 0;
            StartCoroutine(waitBetweenSpooks());
        }
        else
        {
            if (tpAPI != null)
                tpAPI["ircConnectionSendMessage"] = "*HEAVY BREATHING*";
            breathingRef = Audio.PlaySoundAtTransformWithRef("breathing", Module.transform);
            if (tpAPI != null)
            {
                yield return new WaitForSeconds(8);
                _isSpooking = 2;
                yield return new WaitForSeconds(12);
            }
            else
            {
                yield return new WaitForSeconds(5);
                _isSpooking = 2;
                yield return new WaitForSeconds(15);
            }
            _isSpooking = 0;
            StartCoroutine(waitBetweenSpooks());
        }
        _activations++;
        //DebugMsg("Unspooking");
    }

    IEnumerator Unlock(bool hasKey)
    {
        if(hasKey)
        {
            Solve();
            DebugMsg("You've escaped... for now.");
            _hasKey = false;
            KeyGet.SetActive(false);
            PostProcess.gameObject.SetActive(false);
            yield return null;
        } else 
        {
            DebugMsg("I swear I left that key somewhere...");
            KeyCollectSub.GetComponent<Text>().text = "I swear I left that key somewhere...";
            yield return new WaitForSeconds(6);
            KeyCollectSub.GetComponent<Text>().text = "";
        }
    }

    void DebugMsg(string msg)
    {
        Debug.LogFormat("[Three Sentence Horror #{0}] {1}", _moduleId, msg.Replace('\n', ' '));
    }

}

public class TSHSettings
{
    public bool IncludeJumpscares { get; set; }

    public TSHSettings() {}
}