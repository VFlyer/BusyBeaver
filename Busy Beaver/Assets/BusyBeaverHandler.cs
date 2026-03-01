using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.Text.RegularExpressions;
//using Newtonsoft.Json;
using Rnd = UnityEngine.Random;
using KeepCoding;
using KModkit;

public class BusyBeaverHandler : MonoBehaviour {
	
	public KMAudio kmAudio;
    public KMBombModule Module;
    public KMBossModule Boss;
    public KMBombInfo Bomb;
	//public KMModSettings modSettings;

	public TextMesh[] colorblindText;
	public TextMesh displayText, progressText;
	public KMSelectable[] Buttons;
	public KMSelectable submitBtn;
	public MeshRenderer[] bitsRenderer, positionRenderer;
	public Material[] bitStates = new Material[2];
	public Material disableState;

	private const string alphabet = "QWERTYUIOPASDFGHJKLZXCVBNM", easyModeLetters = "ABCDEFGHIJKL";
	private int stageNo, currentIndex, stagesGeneratable, cStage = -1;
	private bool solved = false;

	[SerializeField]
	private bool debugTape;

	private bool[] correctStates = new bool[10], inputStates = new bool[10];
	List<bool[]> displayStates = new List<bool[]>();
	List<int> displayPositions = new List<int>();
	private string assignLetters = "", movementLetters = "";
	private bool inFinale = false, hasStarted = false,
		interactable = true, requestForceSolve = false,
		enableLegacy = false, exhibitionMode = false,
		disableTPToggleBeaver, showHelpingTapes,
		manualRecovery, playAnimation;
	int maxHelpTapesShown = 5;

	private string[] ignoredModules = { // Default ignore list, if it is unable to fetch ignored modules.
		"Busy Beaver",
		"OmegaForget",
		"14",
		"Bamboozling Time Keeper",
		"Brainf---",
		"Forget Enigma",
		"Forget Everything",
		"Forget It Not",
		"Forget Me Not",
		"Forget Me Later",
		"Forget Perspective",
		"Forget The Colors",
		"Forget Them All",
		"Forget This",
		"Forget Us Not",
		"Iconic",
		"Organization",
		"Purgatory",
		"RPS Judging",
		"Simon Forgets",
		"Simon's Stages",
		"Souvenir",
		"Tallordered Keys",
		"The Time Keeper",
		"The Troll",
		"The Twin",
		"The Very Annoying Button",
		"Timing Is Everything",
		"Turn The Key",
		"Ultimate Custom Night",
		"Übermodule"
	};

	static private int _moduleIdCounter = 1;
	private int _moduleId;

	IEnumerator currentMercyAnim;

	BusyBeaverSettings selfSettings = new BusyBeaverSettings();
	// Mission overrides
	static List<BusyBeaverHandler> loadedModules = new List<BusyBeaverHandler>();
	static List<string> overrideStrings = new List<string>();

	void OnDestroy()
    {
		loadedModules.Remove(this);
		if (!loadedModules.Any())
			overrideStrings.Clear();
    }
	// Use this for initialization
	void Start () {
		loadedModules.Add(this);
		_moduleId = _moduleIdCounter++;
		string[] ignoreRepo = Boss.GetIgnoredModules(Module);
		if (ignoreRepo != null && ignoreRepo.Any())
			ignoredModules = ignoreRepo;
		else
		{

			Debug.LogFormat("[Busy Beaver #{0}]: The module uses Boss Module Manager to enforce boss mode onto this module. To prevent softlocks, exhibition mode will be forcably enabled.", _moduleId);
			exhibitionMode = true;
		}
		/* // Old code for using KMModSettings. 
		try
		{
			BusyBeaverSettings obtainedSettings = JsonUtility.FromJson<BusyBeaverSettings>(modSettings.Settings);
			if (obtainedSettings == null)
			{
				Debug.LogFormat("<Busy Beaver Settings>: Unable to find settings! Generating new settings.");
				obtainedSettings = selfSettings;
				selfSettings.enforceExhibitionMode = false;
				selfSettings.legacyMode = false;
			}
			selfSettings = obtainedSettings;
			modSettings.Settings = JsonUtility.ToJson(obtainedSettings, true);
			
			modSettings.RefreshSettings();
			enableLegacy = obtainedSettings.legacyMode;
			exhibitionMode |= obtainedSettings.enforceExhibitionMode;
		}
		catch
		{
			Debug.LogFormat("<Busy Beaver Settings>: Settings do not work as intended! Using default settings!");
			enableLegacy = false;
			//exhibitionMode = false;
		}
		*/
		try
        {
			ModConfig<BusyBeaverSettings> obtainedSettings = new ModConfig<BusyBeaverSettings>("busyBeaverMod-settings");
			selfSettings = obtainedSettings.Settings;
			obtainedSettings.Settings = selfSettings;
			enableLegacy = selfSettings.legacyMode;
			exhibitionMode |= selfSettings.enforceExhibitionMode;
			showHelpingTapes = selfSettings.enableHelpingTapes;
			disableTPToggleBeaver = selfSettings.noTPToggleBeaver;
			manualRecovery = selfSettings.manualRecovery;
			playAnimation = !selfSettings.skipAnimations;
		}
		catch
        {
			Debug.LogWarning("<Busy Beaver Settings>: Settings do not work as intended! Using default settings!");
			enableLegacy = false;
			showHelpingTapes = true;
			//exhibitionMode = false;
			playAnimation = true;
        }
		OverrideSettings();
		storedStartingState = enableLegacy;
		Debug.LogFormat("[Busy Beaver #{0}]: The following will be shown on this module: {1}", _moduleId, enableLegacy ? "modifier letters" : "initial tape + modifier letters (higher chance of identical letters)");
		Module.OnActivate += ActivateModule;
        for (int x = 0; x < Buttons.Length; x++)
        {
            int y = x;
			Buttons[x].OnInteract += delegate {
				kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Buttons[y].transform);
				kmAudio.PlaySoundAtTransform("tick", Buttons[y].transform);
				Buttons[y].AddInteractionPunch(0.1f);
				TogglePos(y);
				return false;
			};
        }
		submitBtn.OnInteract += delegate {
			kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, submitBtn.transform);
			submitBtn.AddInteractionPunch(1f);
			ProcessSubmission();
			return false;
		};

		displayText.text = "";
		progressText.text = "";
		for (int y = 0; y < bitsRenderer.Length; y++)
		{
			bitsRenderer[y].material = disableState;
			colorblindText[y].text = "";
		}
	}
	void TogglePos(int idx)
    {
		if (idx < 0 || idx >= 10) return;
		if (solved || !interactable) return;
		if (inFinale)
		{
			if (currentMercyAnim != null)
				StopCoroutine(currentMercyAnim);
			inputStates[idx] = !inputStates[idx];
			for (int x = 0; x < positionRenderer.Length ; x++)
			{
				bitsRenderer[x].material = inputStates[x] ? bitStates[1] : bitStates[0];
				colorblindText[x].text = inputStates[x] ? "1" : "0";
				colorblindText[x].color = inputStates[x] ? Color.black : Color.white;
				positionRenderer[x].material = disableState;
			}
			progressText.text = "SUBMIT";
			displayText.text = "";
		}
    }

	void ProcessSubmission()
    {
		if (solved || !interactable) return;
		if (inFinale)
        {
			if (correctStates.SequenceEqual(inputStates))
            {
				Debug.LogFormat("[Busy Beaver #{0}]: Correct tape submitted. Module passed.", _moduleId);
				Module.HandlePass();
				StartCoroutine(AnimateDisarmState());
				solved = true;
            }
			else
            {
				Debug.LogFormat("[Busy Beaver #{0}]: Strike! You submitted the following tape: {1}", _moduleId, inputStates.Select(a => a ? "1" : "0").Join(""));
				Module.HandleStrike();
				if (currentMercyAnim != null)
					StopCoroutine(currentMercyAnim);
				currentMercyAnim = enableLegacy ? HandleMercyRevealLegacy() : HandleMercyReveal();
				StartCoroutine(currentMercyAnim);
				kmAudio.PlaySoundAtTransform("strike", transform);
			}
        }
		else if (exhibitionMode)
        {
			cStage++;
			if (cStage > stagesGeneratable || (enableLegacy && cStage + 1 > stagesGeneratable))
			{
				interactable = false;
				if (playAnimation)
					StartCoroutine(AnimateFinaleState());
				else
				{
					for (int x = 0; x < positionRenderer.Length; x++)
					{
						bitsRenderer[x].material = inputStates[x] ? bitStates[1] : bitStates[0];
						colorblindText[x].text = inputStates[x] ? "1" : "0";
						colorblindText[x].color = inputStates[x] ? Color.black : Color.white;
						positionRenderer[x].material = disableState;
					}
					progressText.text = "SUBMIT";
					displayText.text = "";
					inFinale = true;
					interactable = true;
				}
			}
			else
			{
				timeLeft = 3f;
				DisplayCurrentStage();
			}
		}
    }
	void ActivateModule()
    {
		/*
		if (!Application.isEditor)
		{
			stagesGeneratable = Bomb.GetSolvableModuleNames().Count(a => !ignoredModules.Contains(a)) - 1;
		}
		else
		{
			stagesGeneratable = 0;
		}*/
		stagesGeneratable = Bomb.GetSolvableModuleNames().Count(a => !ignoredModules.Contains(a)) - (enableLegacy ? 0 : 1);
		if (stagesGeneratable <= 0)
		{
			Debug.LogFormat("[Busy Beaver #{0}]: There are insufficient non-ignored modules. The module will enter exhibition mode for this.", _moduleId);
			exhibitionMode = true;
			stagesGeneratable = 10;
		}
		else if (exhibitionMode)
		{
			Debug.LogFormat("[Busy Beaver #{0}]: Exhibition mode will avoid advancing stages for each solve for this instance.", _moduleId);
			exhibitionMode = true;
		}
		else
		{
			Debug.LogFormat("[Busy Beaver #{0}]: Total stages generatable: {1} ({2} non-ignored modules detected.)", _moduleId, stagesGeneratable, Bomb.GetSolvableModuleNames().Count(a => !ignoredModules.Contains(a)));
			//StageGeneration();
		}
		GenerateAllStages();
		ProcessAllStages();
		hasStarted = true;
		if (exhibitionMode)
		{
			cStage = 0;
			DisplayCurrentStage();
		}
	}

	bool IsProvidedConditionTrueEasy(char letter)
    {
		// Instructions used for Busy Beaver, the modern set of instructions for this boss module.
		switch (letter)
		{// Note, positions are 0-indexed
			case 'A': return !correctStates[currentIndex];
			case 'B': return currentIndex <= 4;
			case 'C': return correctStates[stageNo % 10];
			case 'D': return correctStates[currentIndex] == correctStates[(currentIndex + 5) % 10];
			case 'E': return correctStates[Mod(currentIndex - 1, 10)] == correctStates[(currentIndex + 1) % 10];
			case 'F': return stageNo % 2 == 1;
			case 'G': return stageNo % 2 == 0;
			case 'H': return correctStates[Mod(currentIndex - 1, 10)] != correctStates[(currentIndex + 1) % 10];
			case 'I': return correctStates[currentIndex] != correctStates[(currentIndex + 5) % 10];
			case 'J': return correctStates[currentIndex];
			case 'K': return currentIndex > 4;
			case 'L': return !correctStates[stageNo % 10];
		}
		return false;
    }
	bool IsProvidedConditionTrue(char letter)
    {
		// Legacy Instructions, created by MaddyMoos before the transfer
		switch (letter)
        {// Note, positions are 0-indexed

			case 'A': return !correctStates[currentIndex];
			case 'B': return currentIndex <= 4;
			case 'C': return correctStates[stageNo % 10];
			case 'D': return (currentIndex + 1) % 2 == 0;
            case 'E': return !correctStates[(stageNo + currentIndex + 1) % 10];
			case 'F': return correctStates[(currentIndex + 5) % 10];
			case 'G': return stageNo % 2 == 0;
			case 'H': return (stageNo + currentIndex + 1) % 2 == 1;
			case 'I': return !correctStates[(stageNo + currentIndex + (correctStates[currentIndex] ? 2 : 1)) % 10];
			case 'J': return (stageNo - (currentIndex + 1) + 10) % 2 == 1;
			case 'K': return ((correctStates[currentIndex] ? 1 : 0) + currentIndex + 1) % 2 == 0;
			case 'L': return (stageNo - (currentIndex + 1 + (correctStates[currentIndex] ? 1 : 0)) + 10) % 2 == 1;
			case 'M': return !correctStates[(stageNo + currentIndex + (correctStates[currentIndex] ? 6 : 5)) % 10];

			case 'N': return correctStates[currentIndex];
			case 'O': return currentIndex >= 5;
			case 'P': return !correctStates[stageNo % 10];
			case 'Q': return (currentIndex + 1) % 2 == 1;
			case 'R': return correctStates[(stageNo + currentIndex + 1) % 10];
			case 'S': return !correctStates[(currentIndex + 5) % 10];
			case 'T': return stageNo % 2 == 1;
			case 'U': return (stageNo + currentIndex + 1) % 2 == 0;
			case 'V': return correctStates[(stageNo + currentIndex + (correctStates[currentIndex] ? 2 : 1)) % 10];
			case 'W': return (stageNo - (currentIndex + 1) + 10) % 2 == 0;
			case 'X': return ((correctStates[currentIndex] ? 1 : 0) + currentIndex + 1) % 2 == 1;
			case 'Y': return (stageNo - (currentIndex + 1 + (correctStates[currentIndex] ? 1 : 0)) + 10) % 2 == 0;
			case 'Z': return correctStates[(stageNo + currentIndex + (correctStates[currentIndex] ? 6 : 5)) % 10];

		}
		return false;
    }

	void GenerateAllStages()
    {
		if (enableLegacy)
			for (int x = 0; x < stagesGeneratable; x++)
			{
				assignLetters += alphabet.PickRandom();
				movementLetters += alphabet.PickRandom();
			}
		else
			for (int x = 0; x < stagesGeneratable; x++)
			{
                if (Rnd.value < 0.5f)
                {
                    char selectedLetter = easyModeLetters.PickRandom();
                    assignLetters += selectedLetter;
                    movementLetters += selectedLetter;
                }
                else
                {
                    assignLetters += easyModeLetters.PickRandom();
                    movementLetters += easyModeLetters.PickRandom();
                }
            }
	}
	void ProcessAllStages()
    {
		bool[] initialState = new bool[10];
		currentIndex = 0;
		if (!enableLegacy)
		{
			for (int y = 0; y < initialState.Length; y++)
			{
				initialState[y] = Rnd.value < 0.5f;
			}
			currentIndex = Rnd.Range(0, 10) % 10;
		}
		correctStates = initialState.ToArray();

		displayStates.Add(initialState);
		displayPositions.Add(currentIndex);

		Debug.LogFormat("[Busy Beaver #{0}]:-----------INITIAL STATE-----------", _moduleId);
		Debug.LogFormat("[Busy Beaver #{0}]: Starting Tape: {1}", _moduleId, correctStates.Select(a => a ? "1" : "0").Join(""));
		Debug.LogFormat("[Busy Beaver #{0}]: Starting Pointer Index: {1}", _moduleId, currentIndex);
		Debug.LogFormat("[Busy Beaver #{0}]:-----------------------------------", _moduleId);
		for (int x = 0; x < Math.Min(assignLetters.Length, movementLetters.Length); x++)
        {
			stageNo++;
			Debug.LogFormat("[Busy Beaver #{0}]:-----------STAGE {1}-----------", _moduleId, stageNo);
			Debug.LogFormat("[Busy Beaver #{0}]: Characters displayed in this stage: \"{1}{2}\"", _moduleId, assignLetters[x], movementLetters[x]);
			bool stateModifer = enableLegacy ? IsProvidedConditionTrue(assignLetters[x]) : IsProvidedConditionTrueEasy(assignLetters[x]),
				moveLeft = enableLegacy ? false : IsProvidedConditionTrueEasy(movementLetters[x]);
			Debug.LogFormat("[Busy Beaver #{0}]: The left character's condition returned {1}", _moduleId, stateModifer);
			if (!enableLegacy)
				Debug.LogFormat("[Busy Beaver #{0}]: The right character's condition returned {1}", _moduleId, moveLeft);
			correctStates[currentIndex] = stateModifer;
			Debug.LogFormat("[Busy Beaver #{0}]: Current Tape: {1}", _moduleId, correctStates.Select(a => a ? "1" : "0").Join(""));
			if (enableLegacy)
			{
				moveLeft = IsProvidedConditionTrue(movementLetters[x]);
				Debug.LogFormat("[Busy Beaver #{0}]: The right character's condition returned {1}", _moduleId, moveLeft);
			}
			currentIndex = Mod(currentIndex + (moveLeft ? -1 : 1), 10);
			Debug.LogFormat("[Busy Beaver #{0}]: Current Pointer Index: {1}", _moduleId, currentIndex);
			if (!enableLegacy && (stageNo == 0 || (showHelpingTapes && stageNo <= Math.Min(maxHelpTapesShown, Math.Min(assignLetters.Length, movementLetters.Length) / 2))) || (debugTape && Application.isEditor))
			{// Check if too many stages have not gone through or half as many stages did not went through already or if the tape should be debugged in the scene AND if legacy mode is NOT enabled
				displayStates.Add(correctStates.ToArray());
				displayPositions.Add(currentIndex);
            }
			Debug.LogFormat("[Busy Beaver #{0}]:-------------------------------", _moduleId);
		}
		Debug.LogFormat("[Busy Beaver #{0}]: Correct Tape to submit: {1}", _moduleId, correctStates.Select(a => a ? "1" : "0").Join(""));
	}
	void DisplayCurrentStage()
    {
		if (enableLegacy)
		{
			displayText.text = string.Format("{0} {1}", assignLetters[cStage], movementLetters[cStage]);
			progressText.text = exhibitionMode ? string.Format("STAGE {0}/{1}->", (1 + cStage).ToString("00"), stagesGeneratable.ToString("00")) : string.Format("STAGE {0}", (cStage + 1).ToString("0000000"));
			for (int x = 0; x < bitsRenderer.Length; x++)
			{
				bitsRenderer[x].material = disableState;
				colorblindText[x].text = "";
			}
			for (int x = 0; x < positionRenderer.Length; x++)
			{
				positionRenderer[x].material = disableState;
			}
		}
		else
		{
			if (cStage > 0)
			{
				displayText.text = string.Format("{0} {1}", assignLetters[cStage - 1], movementLetters[cStage - 1]);
				progressText.text = exhibitionMode ? string.Format("STAGE {0}/{1}->", cStage.ToString("00"), stagesGeneratable.ToString("00")) : string.Format("STAGE {0}", cStage.ToString("0000000"));
			}
			else
			{
				displayText.text = "";
				progressText.text = exhibitionMode ? "START" : "INITIAL";
			}
			if (cStage < displayStates.Count)
			{
				for (int x = 0; x < bitsRenderer.Length; x++)
				{
					bitsRenderer[x].material = displayStates[cStage][x] ? bitStates[1] : bitStates[0];
					colorblindText[x].text = displayStates[cStage][x] ? "1" : "0";
					colorblindText[x].color = displayStates[cStage][x] ? Color.black : Color.white;
				}
				for (int x = 0; x < positionRenderer.Length; x++)
				{
					positionRenderer[x].material = displayPositions[cStage] == x ? bitStates[1] : bitStates[0];
				}
				if (cStage > 0 && !requestForceSolve && playAnimation)
				{
					StartCoroutine(AnimateBlendToNextStatesVisible(displayStates[cStage - 1], displayStates[cStage]));
					StartCoroutine(AnimateBlendToNextPos(displayPositions[cStage - 1], displayPositions[cStage]));
				}
			}
			else
			{
				for (int x = 0; x < bitsRenderer.Length; x++)
				{
					bitsRenderer[x].material = disableState;
					colorblindText[x].text = "";
				}
				for (int x = 0; x < positionRenderer.Length; x++)
				{
					positionRenderer[x].material = disableState;
				}
				if (cStage - 1 < displayStates.Count && !requestForceSolve && playAnimation)
				{
					StartCoroutine(AnimateDisableStates(displayStates[cStage - 1]));
					StartCoroutine(AnimateDisablePosition(displayPositions[cStage - 1]));
					kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.LightBuzzShort, transform);
				}
			}
		}
	}
	IEnumerator AnimateBlendToNextStatesVisible(bool[] previousState, bool[] nextState)
    {
		bool[] canChange = new bool[Math.Min(previousState.Length, nextState.Length)];
        for (int x = 0; x < Math.Min(previousState.Length, nextState.Length); x++)
        {
			canChange[x] = previousState[x] != nextState[x];
        }
		if (!canChange.ToList().TrueForAll(a => !a))
		for (float x = 0; x <= 1f; x = Math.Min(x + 10 * Time.deltaTime, 1f))
        {
            for (int y = 0; y < canChange.Length; y++)
            {
				if (canChange[y] && bitsRenderer[y].material.HasProperty("_Blend"))
					bitsRenderer[y].material.SetFloat("_Blend", 1f - x);
            }
			if (x == 1f) break;
			yield return new WaitForSeconds(Time.deltaTime);
        }
		yield return null;
    }
	IEnumerator AnimateBlendToNextPos(int originalPos,int newPos)
    {
		if (originalPos == newPos) yield break;

		bool[] canChange = new bool[10];
		for (int x = 0; x < canChange.Length; x++)
		{
			canChange[x] = x == originalPos || x == newPos;
		}
		for (float x = 0; x <= 1f; x = Math.Min(x + 10 * Time.deltaTime, 1f))
		{
			for (int y = 0; y < positionRenderer.Length; y++)
			{
				if (canChange[y] && positionRenderer[y].material.HasProperty("_Blend"))
					positionRenderer[y].material.SetFloat("_Blend", 1f - x);
			}
			if (x == 1f) break;
			yield return new WaitForSeconds(Time.deltaTime);
		}
		yield return null;
    }
	float[] flickerTimings = { 0.1f, 0.5f, 0.1f, 0.2f, 0.1f };
	IEnumerator AnimateDisableStates(bool[] previousStates)
    {
        for (int x = 0; x < flickerTimings.Length; x++)
		{
			for (int y = 0; y < previousStates.Length; y++)
			{
				bitsRenderer[y].material = x % 2 == 0 ? disableState : previousStates[y] ? bitStates[1] : bitStates[0];
				colorblindText[y].text = x % 2 == 0 ? "" : previousStates[y] ? "1" : "0";
				colorblindText[y].color = previousStates[y] ? Color.black : Color.white;
			}
			yield return new WaitForSeconds(flickerTimings[x]);
		}
	}
	IEnumerator AnimateDisablePosition(int previousPosition)
	{
		for (int x = 0; x < flickerTimings.Length; x++)
		{
			for (int y = 0; y < positionRenderer.Length; y++)
			{
				positionRenderer[y].material = x % 2 == 0 ? disableState : y == previousPosition ? bitStates[1] : bitStates[0];
			}
			yield return new WaitForSeconds(flickerTimings[x]);
		}
	}
	IEnumerator HandleMercyReveal()
	{
        displayText.text = string.Format("{0} C", Enumerable.Range(0, 10).Count(a => inputStates[a] == correctStates[a]));
		for (int x = 0; x <= 4; x++)
		{
			displayText.color = x % 2 == 0 ? Color.white : Color.red;
			yield return new WaitForSeconds(0.2f);
		}
		var firstLoop = true;
		while (enabled)
		{
			for (int z = 0; z < 1 + movementLetters.Length; z++)
			{
				if (z > 0)
				{
					displayText.text = string.Format("{0} {1}", assignLetters[z - 1], movementLetters[z - 1]);
					progressText.text = string.Format("STAGE {0}", z.ToString("0000000"));
				}
				else
				{
					displayText.text = "";
					progressText.text = "INITIAL";
				}
				if (z < displayStates.Count)
				{
					for (int x = 0; x < bitsRenderer.Length; x++)
					{
						bitsRenderer[x].material = displayStates[z][x] ? bitStates[1] : bitStates[0];
						colorblindText[x].text = displayStates[z][x] ? "1" : "0";
						colorblindText[x].color = displayStates[z][x] ? Color.black : Color.white;
					}
					for (int x = 0; x < positionRenderer.Length; x++)
					{
						positionRenderer[x].material = displayPositions[z] == x ? bitStates[1] : bitStates[0];
					}
					if (z > 0)
					{
						StartCoroutine(AnimateBlendToNextPos(displayPositions[z - 1], displayPositions[z]));
						StartCoroutine(AnimateBlendToNextStatesVisible(displayStates[z - 1], displayStates[z]));
					}
					yield return new WaitForSeconds(4f);
				}
				else
				{
					for (int x = 0; x < bitsRenderer.Length; x++)
					{
						bitsRenderer[x].material = disableState;
						colorblindText[x].text = "";
					}
					for (int x = 0; x < positionRenderer.Length; x++)
					{
						positionRenderer[x].material = disableState;
					}
					if (z - 1 < displayStates.Count)
					{
						StartCoroutine(AnimateDisablePosition(displayPositions[z - 1]));
						StartCoroutine(AnimateDisableStates(displayStates[z - 1]));
						if (firstLoop)
						{
							kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.LightBuzzShort, transform);
							firstLoop = false;
						}
					}
					yield return new WaitForSeconds(2f);
				}
			}
			while (displayText.text.Length > 0 || progressText.text.Length > 0)
			{
				if (!string.IsNullOrEmpty(displayText.text))
					displayText.text = displayText.text.Substring(0, displayText.text.Length - 1).Trim();
				if (!string.IsNullOrEmpty(progressText.text))
					progressText.text = progressText.text.Substring(0, progressText.text.Length - 1).Trim();
				yield return new WaitForSeconds(.05f);
			}
			for (int x = 0; x < positionRenderer.Length / 2; x++)
			{
				bitsRenderer[x].material = displayStates[0][x] ? bitStates[1] : bitStates[0];
				colorblindText[x].text = displayStates[0][x] ? "1" : "0";
				colorblindText[x].color = displayStates[0][x] ? Color.black : Color.white;
				positionRenderer[x].material = displayPositions[0] == x ? bitStates[1] : bitStates[0];

				bitsRenderer[9 - x].material = displayStates[0][9 - x] ? bitStates[1] : bitStates[0];
				colorblindText[9 - x].text = displayStates[0][9 - x] ? "1" : "0";
				colorblindText[9 - x].color = displayStates[0][9 - x] ? Color.black : Color.white;
				positionRenderer[9 - x].material = displayPositions[0] == 9 - x ? bitStates[1] : bitStates[0];

				yield return new WaitForSeconds(0.05f);
			}
		}
		/*
		for (int x = 0; x < positionRenderer.Length / 2; x++)
		{
			bitsRenderer[x].material = inputStates[x] ? bitStates[1] : bitStates[0];
			colorblindText[x].text = inputStates[x] ? "1" : "0";
			colorblindText[x].color = inputStates[x] ? Color.black : Color.white;
			positionRenderer[x].material = disableState;

			bitsRenderer[9 - x].material = inputStates[9 - x] ? bitStates[1] : bitStates[0];
			colorblindText[9 - x].text = inputStates[9 - x] ? "1" : "0";
			colorblindText[9 - x].color = inputStates[9 - x] ? Color.black : Color.white;
			positionRenderer[9 - x].material = disableState;

			yield return new WaitForSeconds(0.05f);
		}
		string subText = "SUBMIT";
		for (int x = subText.Length - 1; x >= 0; x--)
		{
			kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.TypewriterKey, transform);
			progressText.text = subText.Substring(x);
			yield return new WaitForSeconds(0.05f);
		}
		yield return null;
		*/
	}
	IEnumerator HandleMercyRevealLegacy()
    {
        displayText.text = "? C";
		for (int x = 0; x <= 4; x++)
		{
			displayText.color = x % 2 == 0 ? Color.white : Color.red;
			yield return new WaitForSeconds(0.2f);
		}
		while (enabled)
		{
			for (int z = 0; z < movementLetters.Length; z++)
			{
				displayText.text = string.Format("{0} {1}", assignLetters[z], movementLetters[z]);
				progressText.text = string.Format("STAGE {0}", (z + 1).ToString("0000000"));
				yield return new WaitForSeconds(2f);
			}
			while (displayText.text.Length > 0 || progressText.text.Length > 0)
			{
				if (!string.IsNullOrEmpty(displayText.text))
					displayText.text = displayText.text.Substring(0, displayText.text.Length - 1).Trim();
				if (!string.IsNullOrEmpty(progressText.text))
					progressText.text = progressText.text.Substring(0, progressText.text.Length - 1).Trim();
				yield return new WaitForSeconds(.05f);
			}
		}
		/*
		string subText = "SUBMIT";
		for (int x = subText.Length - 1; x >= 0; x--)
		{
			kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.TypewriterKey, transform);
			progressText.text = subText.Substring(x);
			yield return new WaitForSeconds(0.05f);
		}
		yield return null;
		*/
    }
	string[] possibleDisarmTexts = {
		"MODULE DONE",
		"MODULE SOLVED",
		"SOLVED",
		"THERE WE GO",
		"WELL DONE",
		"CONGRATS",
		"DISARMED",
		"YOU DID IT"
	};
	IEnumerator AnimateDisarmState()
	{
		string selectedText = possibleDisarmTexts.PickRandom();
		displayText.text = "";
		float[] delayTimes = { 0.8f, 0.1f, 0.6f, 0.1f, 0.4f, 0.1f, 0.2f, 0.1f, 0.1f, 0.1f };
		kmAudio.PlaySoundAtTransform("321107__nsstudios__robot-or-machine-destroy", transform);
		for (int x = 0; x < delayTimes.Length; x++)
		{
			for (int y = 0; y < positionRenderer.Length; y++)
			{
				bitsRenderer[y].material = x % 2 == 1 ? disableState : inputStates[y] ? bitStates[1] : bitStates[0];
				colorblindText[y].text = x % 2 == 1 ? "" : inputStates[y] ? "1" : "0";
				colorblindText[y].color = inputStates[y] ? Color.black : Color.white;
			}
			progressText.text = x % 2 == 1 ? "" : selectedText;
			yield return new WaitForSeconds(delayTimes[x]);
		}
	}
	IEnumerator AnimateFinaleState()
    {
		while (displayText.text.Length > 0 || progressText.text.Length > 0)
        {
			if (!string.IsNullOrEmpty(displayText.text))
				displayText.text = displayText.text.Substring(0, displayText.text.Length - 1).Trim();
			if (!string.IsNullOrEmpty(progressText.text))
				progressText.text = progressText.text.Substring(0, progressText.text.Length - 1).Trim();
			kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.TypewriterKey, transform);
			yield return new WaitForSeconds(.025f);
		}

		for (int x = 0; x < positionRenderer.Length / 2; x++)
		{
			bitsRenderer[x].material = inputStates[x] ? bitStates[1] : bitStates[0];
			colorblindText[x].text = inputStates[x] ? "1" : "0";
			colorblindText[x].color = inputStates[x] ? Color.black : Color.white;
			positionRenderer[x].material = disableState;

			bitsRenderer[9 - x].material = inputStates[9 - x] ? bitStates[1] : bitStates[0];
            colorblindText[9 - x].text = inputStates[9 - x] ? "1" : "0";
			colorblindText[9 - x].color = inputStates[9 - x] ? Color.black : Color.white;
			positionRenderer[9 - x].material = disableState;

			yield return new WaitForSeconds(0.05f);
		}
		string subText = "SUBMIT";
        for (int x = subText.Length - 1; x >= 0; x--)
        {
			kmAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.TypewriterKey, transform);
			progressText.text = subText.Substring(x);
			yield return new WaitForSeconds(0.05f);
		}
		inFinale = true;
		interactable = true;
    }
	

	void OverrideSettings()
    {
		// Override settings via mission description.
		var missionDescription = Game.Mission.Description ?? "";
		var keywords = new[] { "Legacy", "Exhibition", "Normal", "Boss", "Modern", "Helpless", "Helpful" };
        var regexOverrideDescAll = Regex.Matches(missionDescription, string.Format(@"[BusyBeaver](\s({0}|H\d+))+", keywords.Join("|")));
		if (!overrideStrings.Any())
			foreach (Match item in regexOverrideDescAll)
				overrideStrings.Add(item.Value);
		var regexOverrideDesc = overrideStrings.ElementAtOrDefault(loadedModules.IndexOf(this));
		if (!string.IsNullOrEmpty(regexOverrideDesc))
        {
			var matchingString = regexOverrideDesc;
			var splittedArguments = matchingString.Split().Skip(1);
			foreach (var value in splittedArguments)
            {
				switch (value)
                {
					case "Legacy":
						enableLegacy = true;
						disableTPToggleBeaver = true;
						break;
					case "Modern":
					case "Normal":
						enableLegacy = false;
						disableTPToggleBeaver = true;
						break;
					case "Exhibition":
						exhibitionMode = true;
						break;
					case "Boss":
						exhibitionMode = false;
						break;
					case "Helpless":
						showHelpingTapes = false;
						break;
					case "Helpful":
						showHelpingTapes = true;
						break;
					default: // H###
                        {
							int stagesProcessed;
							if (int.TryParse(value.Substring(1),out stagesProcessed))
								maxHelpTapesShown = stagesProcessed;
                        }
						break;
                }
            }
			Debug.LogFormat("[Busy Beaver #{0}]: Module's settings have been overriden with the following arguments: {1}", _moduleId, splittedArguments.Join(","));
        }

    }
	// Update is called once per frame
	float timeLeft = 0f;
	void Update () {
		if (!inFinale && hasStarted && !exhibitionMode)
			if (timeLeft <= 0f && cStage < Bomb.GetSolvedModuleNames().Where(a => !ignoredModules.Contains(a)).Count() && !solved)
			{
				cStage++;
				if (!inFinale && ((cStage >= stagesGeneratable && enableLegacy) || (cStage > stagesGeneratable)))
				{
					Debug.LogFormat("[Busy Beaver #{0}]: Its time to input. Here we go.", _moduleId);
					if (playAnimation)
						StartCoroutine(AnimateFinaleState());
					else
                    {
						for (int x = 0; x < positionRenderer.Length; x++)
						{
							bitsRenderer[x].material = inputStates[x] ? bitStates[1] : bitStates[0];
							colorblindText[x].text = inputStates[x] ? "1" : "0";
							colorblindText[x].color = inputStates[x] ? Color.black : Color.white;
							positionRenderer[x].material = disableState;
						}
						progressText.text = "SUBMIT";
						inFinale = true;
						interactable = true;
					}
				}
				else
				{
					timeLeft = 3f;
					DisplayCurrentStage();
				}
			}
			else
			{
				timeLeft = requestForceSolve ? 0f : Mathf.Max(timeLeft - Time.deltaTime, 0);
			}
	}
	private int Mod(int num, int mod) {
		return ((num % mod) + mod) % mod;
	}
	// TP section begins here
	IEnumerator AnimateToggleBeaver()
    {
		Debug.LogFormat("[Busy Beaver #{0}]: TP Requsted to toggle Busy Beaver's current mode! Resetting calculations.", _moduleId);
		while (displayText.text.Length > 0 || progressText.text.Length > 0)
		{
			if (!string.IsNullOrEmpty(displayText.text))
				displayText.text = displayText.text.Substring(0, displayText.text.Length - 1).Trim();
			if (!string.IsNullOrEmpty(progressText.text))
				progressText.text = progressText.text.Substring(0, progressText.text.Length - 1).Trim();
			yield return new WaitForSeconds(.025f);
		}
		for (int x = 0; x < positionRenderer.Length / 2; x++)
		{
			bitsRenderer[x].material = disableState;
			colorblindText[x].text = "";
			positionRenderer[x].material = disableState;

			bitsRenderer[9 - x].material = disableState;
			colorblindText[9 - x].text = "";
			positionRenderer[9 - x].material = disableState;

			yield return new WaitForSeconds(0.05f);
		}
		enableLegacy ^= true;
		hasStarted = false;
		assignLetters = "";
		movementLetters = "";
		displayPositions.Clear();
		displayStates.Clear();
		stageNo = 0;
		cStage = -1;
		ActivateModule();		
		//DisplayCurrentStage();
		interactable = true;
		yield break;
    }

	bool enableToggleBeaver, storedStartingState;
	#pragma warning disable 414
		private readonly string TwitchHelpMessage = "Submit the binary sequence using \"!{0} submit 1101001010\" (In this example, set the binary to 1101001010, then presses the submit button.) 'T'/'F' can be used for 1's and 0's instead. You may space out the binary digits in the command." +
		"To advance to the next stage, use \"!{0} advance/next\" (Only if in Exhibition Mode) You may append a number to specify how many stages to press next on.";
	#pragma warning restore 414
	IEnumerator DelayToggle()
    {
		
		yield return new WaitForSeconds(5);
		enableToggleBeaver = false;
		yield break;
    }
	IEnumerator ProcessTwitchCommand(string command)
    {
		if (!interactable)
        {
			yield return "sendtochaterror The module cannot be interacted right now. Wait a bit until you can interact with this.";
			yield break;
		}
        Match subCmd = Regex.Match(command, @"^\s*submit\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
			advCmd = Regex.Match(command,@"^\s*(advance|next)(\s\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
			toggleBossCmd = Regex.Match(command, @"^(giveme|gimmie)otherbeaver$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (toggleBossCmd.Success)
        {
			if (disableTPToggleBeaver)
            {
				yield return "sendtochat {0}, I cannot let you do that due to settings/overrides for this module preventing this.";
				yield break;
			}
			if (storedStartingState ^ enableLegacy)
            {
				yield return "sendtochat The module is already in the other mode. I suggest you get this exploded for now if you want to toggle it back.";
				yield break;
			}
			if (cStage > 0)
            {
				yield return "sendtochat A non-ignored module has already been solved or a stage has been advanced. You cannot toggle this anymore.";
				yield break;
			}
			if (enableToggleBeaver)
            {
				yield return null;
				yield return "sendtochat {0}, you asked for it.";
				StartCoroutine(AnimateToggleBeaver());
				interactable = false;
				yield break;
            }
			enableToggleBeaver = true;
			yield return "sendtochat Are you sure you want to toggle Busy Beaver's current mode? Type in the same command within 5 seconds to confirm and DO NOT have any non-ingored modules solved when doing this.";
			StartCoroutine(DelayToggle());
			yield break;
		}
		else if (subCmd.Success)
        {
			if (!inFinale)
			{
				yield return "sendtochaterror The module is not ready to submit. Advance all the stages first or solve enough modules to use this command.";
				yield break;
			}
			var Binary = command.Trim().Substring(7).Trim().ToLower().Replace('t','1').Replace('f', '0').Split();
			//Debug.Log(Binary.Join(", "));
			List<bool> binaryStates = new List<bool>();
			for (int x = 0; x < Binary.Length; x++)
			{
				foreach (char suggestedLetter in Binary[x])
					switch (suggestedLetter)
					{
						case '0':
							binaryStates.Add(false);
							break;
						case '1':
							binaryStates.Add(true);
							break;
						default:
							yield return string.Format("sendtochaterror I do not know what character \"{0}\" releates to in binary. Recheck your command.", Binary[x]);
							yield break;
					}
			}
			if (binaryStates.Count != 10)
			{
				yield return string.Format("sendtochaterror You provided {0} binary digit(s) when I expected exactly 10. Recheck your command.",binaryStates.Count);
				yield break;
			}
            yield return null;  // acknowledge to TP that the command was valid

            for (int i = 0; i < binaryStates.Count; i++)
            {
				if (inputStates[i] != binaryStates[i])
				{
					Buttons[i].OnInteract();
					yield return new WaitForSeconds(.1f);
				}
            }
            submitBtn.OnInteract();
			yield break;
        }
		else if (advCmd.Success)
        {
			if (!exhibitionMode)
            {
				yield return "sendtochaterror The module is not in its special phase. This command is useless when there are enough non-ignored modules.";
				yield break;
			}
			if (inFinale)
            {
				yield return "sendtochaterror The module is awaiting submission. Pressing just \"SUBMIT\" would definitely strike you from here.";
				yield break;
			}
			string intereptedCommand = advCmd.Value.Substring(advCmd.Value.ToLower().StartsWith("next") ? 4 : 7);
			if (string.IsNullOrEmpty(intereptedCommand))
			{
				yield return null;
				submitBtn.OnInteract();
			}
			else
            {
				intereptedCommand = intereptedCommand.Trim();
				int repeatTimes;
				if (!int.TryParse(intereptedCommand, out repeatTimes) || repeatTimes <= 0)
                {
					yield return string.Format("sendtochaterror I do not know how to interact with a given button \"{0}\" times.", intereptedCommand);
					yield break;
				}
				for (int x = 0; x < repeatTimes; x++)
				{
					if (!interactable)
                    {
						yield return string.Format("sendtochaterror The module stopped allowing inputs to process after \"{0}\" presses.", x);
						yield break;
					}
					yield return null;
					submitBtn.OnInteract();
					yield return new WaitForSeconds(1f);
				}
			}
        }
    }
	IEnumerator TwitchHandleForcedSolve()
    {
		requestForceSolve = true;
		if (exhibitionMode && !inFinale)
        {
			while (interactable)
            {
				submitBtn.OnInteract();
				yield return null;
			}
        }
		while (!inFinale) //Wait until submission time
            yield return true;
        for (int i = 0; i < 10; i++)
        {
			if(correctStates[i] != inputStates[i])
				Buttons[i].OnInteract();
			yield return null;
		}
		submitBtn.OnInteract();
		yield return true;
    }
	// Settings handler.
	public class BusyBeaverSettings
    {
		public bool legacyMode = false;
		public bool enforceExhibitionMode = false;
		public bool manualRecovery = false;
		public bool enableHelpingTapes = true;
		public bool noTPToggleBeaver = false;
		public bool skipAnimations = false;
	}
}
