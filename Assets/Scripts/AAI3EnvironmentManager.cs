using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using ArenasParameters;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.SideChannels;
using Unity.MLAgents.Policies;

/// <summary>
/// Manages the environment settings and configurations for the AAI project.
/// </summary>
public class AAI3EnvironmentManager : MonoBehaviour
{
	[Header("Arena Settings")]
	[SerializeField]
	private GameObject arena;

	[SerializeField]
	private GameObject uiCanvas;

	[SerializeField]
	private GameObject playerControls;

	[Header("Configuration File")]
	[SerializeField]
	private string configFile = "";

	[Header("Resolution Settings")]
	[SerializeField]
	private const int maximumResolution = 512;

	[SerializeField]
	private const int minimumResolution = 4;

	[SerializeField]
	private const int defaultResolution = 84;

	[SerializeField]
	private const int defaultRaysPerSide = 2;

	[SerializeField]
	private const int defaultRayMaxDegrees = 60;

	[SerializeField]
	private const int defaultDecisionPeriod = 3;

	public bool PlayerMode { get; private set; } = true;

	private ArenasConfigurations _arenasConfigurations;
	private TrainingArena _instantiatedArena;
	private ArenasParametersSideChannel _arenasParametersSideChannel;

	public static event Action<int, int> OnArenaChanged;

	#region Initialisation Methods
	public void Awake()
	{
		InitialiseSideChannel();

		Dictionary<string, int> environmentParameters = RetrieveEnvironmentParameters();
		int paramValue;
		bool playerMode =
			(environmentParameters.TryGetValue("playerMode", out paramValue) ? paramValue : 1) > 0;
		bool useCamera =
			(environmentParameters.TryGetValue("useCamera", out paramValue) ? paramValue : 0) > 0;
		int resolution = environmentParameters.TryGetValue("resolution", out paramValue)
			? paramValue
			: defaultResolution;
		bool grayscale =
			(environmentParameters.TryGetValue("grayscale", out paramValue) ? paramValue : 0) > 0;
		bool useRayCasts =
			(environmentParameters.TryGetValue("useRayCasts", out paramValue) ? paramValue : 0) > 0;
		int raysPerSide = environmentParameters.TryGetValue("raysPerSide", out paramValue)
			? paramValue
			: defaultRaysPerSide;
		int rayMaxDegrees = environmentParameters.TryGetValue("rayMaxDegrees", out paramValue)
			? paramValue
			: defaultRayMaxDegrees;
		int decisionPeriod = environmentParameters.TryGetValue("decisionPeriod", out paramValue)
			? paramValue
			: defaultDecisionPeriod;
		Debug.Log("Set playermode to " + playerMode);

		if (Application.isEditor)
		{
			Debug.Log("Using Unity Editor Default Configuration");
			playerMode = true;
			useCamera = true;
			resolution = 84;
			grayscale = false;
			useRayCasts = true;
			raysPerSide = 2;

			LoadYAMLFileInEditor();
		}

		resolution = Math.Max(minimumResolution, Math.Min(maximumResolution, resolution));
		TrainingArena arena = FindObjectOfType<TrainingArena>();

		InstantiateArenas();

		playerControls.SetActive(playerMode);
		uiCanvas.GetComponent<Canvas>().enabled = playerMode;

		foreach (Agent a in FindObjectsOfType<Agent>(true))
		{
			a.GetComponentInChildren<DecisionRequester>().DecisionPeriod = decisionPeriod;
			if (!useRayCasts)
			{
				DestroyImmediate(a.GetComponentInChildren<RayPerceptionSensorComponent3D>());
			}
			else
			{
				ChangeRayCasts(
					a.GetComponentInChildren<RayPerceptionSensorComponent3D>(),
					raysPerSide,
					rayMaxDegrees
				);
			}
			if (!useCamera)
			{
				DestroyImmediate(a.GetComponentInChildren<CameraSensorComponent>());
			}
			else
			{
				ChangeResolution(
					a.GetComponentInChildren<CameraSensorComponent>(),
					resolution,
					resolution,
					grayscale
				);
			}
			if (playerMode)
			{
				a.GetComponentInChildren<BehaviorParameters>().BehaviorType =
					BehaviorType.HeuristicOnly;
			}
		}
		PrintDebugInfo(
			playerMode,
			useCamera,
			resolution,
			grayscale,
			useRayCasts,
			raysPerSide,
			rayMaxDegrees
		);
		_instantiatedArena._agent.gameObject.SetActive(true);
	}

	private void InitialiseSideChannel()
	{
		_arenasConfigurations = new ArenasConfigurations();
		_arenasParametersSideChannel = new ArenasParametersSideChannel();
		_arenasParametersSideChannel.NewArenasParametersReceived +=
			_arenasConfigurations.UpdateWithConfigurationsReceived;
		SideChannelManager.RegisterSideChannel(_arenasParametersSideChannel);
	}

	private void InstantiateArenas()
	{
		GameObject arenaInst = Instantiate(arena, new Vector3(0f, 0f, 0f), Quaternion.identity);
		_instantiatedArena = arenaInst.GetComponent<TrainingArena>();
		_instantiatedArena.arenaID = 0;
	}

	private void LoadYAMLFileInEditor()
	{
		if (string.IsNullOrWhiteSpace(configFile))
		{
			Debug.LogWarning("Config file path is null or empty.");
			return;
		}

		try
		{
			var configYAML = Resources.Load<TextAsset>(configFile);
			if (configYAML != null)
			{
				var YAMLReader = new YAMLDefs.YAMLReader();
				var parsed = YAMLReader.deserializer.Deserialize<YAMLDefs.ArenaConfig>(
					configYAML.text
				);
				if (parsed != null)
				{
					_arenasConfigurations.UpdateWithYAML(parsed);
				}
				else
				{
					Debug.LogWarning("Deserialized YAML content is null.");
				}
			}
			else
			{
				Debug.LogWarning($"YAML file '{configFile}' could not be found or loaded.");
			}
		}
		catch (Exception ex)
		{
			Debug.LogError(
				$"An error occurred while loading or processing the YAML file: {ex.Message}"
			);
		}
	}

	#endregion

	#region Public Getter/Setter Methods

	public void TriggerArenaChangeEvent(int currentArenaIndex, int totalArenas)
	{
		OnArenaChanged?.Invoke(currentArenaIndex, totalArenas);
	}

	public bool GetRandomizeArenasStatus()
	{
		return _arenasConfigurations.randomizeArenas;
	}

	public int GetCurrentArenaIndex()
	{
		return _arenasConfigurations.CurrentArenaID;
	}

	public int GetTotalArenas()
	{
		return _arenasConfigurations.configurations.Count;
	}

	#endregion

	#region Environment Configuration Methods
	private void ChangeRayCasts(
		RayPerceptionSensorComponent3D raySensor,
		int no_raycasts,
		int max_degrees
	)
	{
		raySensor.RaysPerDirection = no_raycasts;
		raySensor.MaxRayDegrees = max_degrees;
	}

	private void ChangeResolution(
		CameraSensorComponent cameraSensor,
		int cameraWidth,
		int cameraHeight,
		bool grayscale
	)
	{
		cameraSensor.Width = cameraWidth;
		cameraSensor.Height = cameraHeight;
		cameraSensor.Grayscale = grayscale;
	}

	private Dictionary<string, int> RetrieveEnvironmentParameters()
	{
		Dictionary<string, int> environmentParameters = new Dictionary<string, int>();
		string[] args = System.Environment.GetCommandLineArgs();
		Debug.Log("Command Line Args: " + String.Join(" ", args));

		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--playerMode":
					int playerMode = (i < args.Length - 1) ? Int32.Parse(args[i + 1]) : 1;
					environmentParameters.Add("playerMode", playerMode);
					break;
				case "--receiveConfiguration":
					environmentParameters.Add("receiveConfiguration", 0);
					break;
				case "--numberOfArenas":
					int nArenas = (i < args.Length - 1) ? Int32.Parse(args[i + 1]) : 1;
					environmentParameters.Add("numberOfArenas", nArenas);
					break;
				case "--useCamera":
					environmentParameters.Add("useCamera", 1);
					break;
				case "--resolution":
					int camW = (i < args.Length - 1) ? Int32.Parse(args[i + 1]) : defaultResolution;
					environmentParameters.Add("resolution", camW);
					break;
				case "--grayscale":
					environmentParameters.Add("grayscale", 1);
					break;
				case "--useRayCasts":
					environmentParameters.Add("useRayCasts", 1);
					break;
				case "--raysPerSide":
					int rps = (i < args.Length - 1) ? Int32.Parse(args[i + 1]) : 2;
					environmentParameters.Add("raysPerSide", rps);
					break;
				case "--rayMaxDegrees":
					int rmd = (i < args.Length - 1) ? Int32.Parse(args[i + 1]) : 60;
					environmentParameters.Add("rayMaxDegrees", rmd);
					break;
				case "--decisionPeriod":
					int dp = (i < args.Length - 1) ? Int32.Parse(args[i + 1]) : 3;
					environmentParameters.Add("decisionPeriod", dp);
					break;
			}
		}
		return environmentParameters;
	}

	#endregion

	#region Configuration Management Methods

	public ArenaConfiguration GetConfiguration(int arenaID)
	{
		ArenaConfiguration returnConfiguration;
		if (!_arenasConfigurations.configurations.TryGetValue(arenaID, out returnConfiguration))
		{
			throw new KeyNotFoundException($"Tried to load arena {arenaID} but it did not exist");
		}
		return returnConfiguration;
	}

	public void AddConfiguration(int arenaID, ArenaConfiguration arenaConfiguration)
	{
		_arenasConfigurations.configurations.Add(arenaID, arenaConfiguration);
	}

	#endregion

	#region Other Methods

	public void OnDestroy()
	{
		if (Academy.IsInitialized)
		{
			SideChannelManager.UnregisterSideChannel(_arenasParametersSideChannel);
		}
	}

	private void PrintDebugInfo(
		bool playerMode,
		bool useCamera,
		int resolution,
		bool grayscale,
		bool useRayCasts,
		int raysPerSide,
		int rayMaxDegrees
	)
	{
		Debug.Log(
			"Environment loaded with options:"
				+ "\n  PlayerMode: "
				+ playerMode
				+ "\n  useCamera: "
				+ useCamera
				+ "\n  Resolution: "
				+ resolution
				+ "\n  grayscale: "
				+ grayscale
				+ "\n  useRayCasts: "
				+ useRayCasts
				+ "\n  raysPerSide: "
				+ raysPerSide
				+ "\n  rayMaxDegrees: "
				+ rayMaxDegrees
		);
	}

	#endregion

	#region Read Stream

	public static byte[] ReadFully(Stream stream)
	{
		using var ms = new MemoryStream();
		stream.CopyTo(ms);
		return ms.ToArray();
	}

	#endregion
}
