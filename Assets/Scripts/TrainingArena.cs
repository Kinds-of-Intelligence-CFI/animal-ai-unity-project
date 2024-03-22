using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using ArenaBuilders;
using UnityEngineExtensions;
using ArenasParameters;
using Holders;
using Random = UnityEngine.Random;
using System.Linq;

/// <summary>
/// This class is responsible for managing the training arena. 
/// It contains the logic to reset the arena, update the light status, and handle the spawning of rewards.
/// It also initializes the components required for the arena, such as the ArenaBuilder and the AAI3EnvironmentManager.
/// </summary>
public class TrainingArena : MonoBehaviour
{
	[SerializeField]
	private ListOfPrefabs prefabs;
	[SerializeField]
	private GameObject spawnedObjectsHolder;
	[SerializeField]
	private int maxSpawnAttemptsForAgent = 100;
	[SerializeField]
	private int maxSpawnAttemptsForPrefabs = 20;
	[SerializeField]
	private ListOfBlackScreens blackScreens;

	[HideInInspector]
	public int arenaID = -1;

	public TrainingAgent _agent;

	private ArenaBuilder _builder;
	private ArenaConfiguration _arenaConfiguration = new ArenaConfiguration();
	private AAI3EnvironmentManager _environmentManager;
	private List<Fade> _fades = new List<Fade>();
	private bool _lightStatus = true;
	private int _agentDecisionInterval;
	private bool isFirstArenaReset = true;
	private List<GameObject> spawnedRewards = new List<GameObject>();
	private List<int> playedArenas = new List<int>();
	private List<int> _mergedArenas = null;

	public bool showNotification { get; set; }
	public bool mergeNextArena
	{
		get {
			return _arenaConfiguration.mergeNextArena;
		}
	}

	public ArenaBuilder Builder
	{
		get { return _builder; }
	}

	public ArenaConfiguration ArenaConfig
	{
		get { return _arenaConfiguration; }
	}

	internal void Awake()
	{
		InitializeArenaComponents();
	}

	void FixedUpdate()
	{
		UpdateLigthStatus();
	}

	private void OnDestroy()
	{
		Spawner_InteractiveButton.RewardSpawned -= OnRewardSpawned;
	}

	/// <summary>
	/// Initializes the components required for the arena, such as the ArenaBuilder and the AAI3EnvironmentManager.
	/// </summary>
	private void InitializeArenaComponents()
	{
		_builder = new ArenaBuilder(
		gameObject,
		spawnedObjectsHolder,
		maxSpawnAttemptsForPrefabs,
		maxSpawnAttemptsForAgent
		);
		_environmentManager = GameObject.FindObjectOfType<AAI3EnvironmentManager>();
		_agent = FindObjectsOfType<TrainingAgent>(true)[0];
		_agentDecisionInterval = _agent.GetComponentInChildren<DecisionRequester>().DecisionPeriod;
		_fades = blackScreens.GetFades();

		Spawner_InteractiveButton.RewardSpawned += OnRewardSpawned;
	}

	/// <summary>
	/// Provides a list of the arenas in the current config file that are preceeded by an arena with
	/// the mergeNextArena property, so that we can avoid loading them when arenas are randomised.
	/// </summary>
	private List<int> GetMergedArenas()
	{
		List<int> mergedArenas = new List<int>();
		int totalArenas = _environmentManager.getArenaCount();
		ArenaConfiguration currentArena;
		if (!_environmentManager.GetConfiguration(0, out currentArena))
		{
			Debug.LogError("Critical error: Failed to load arena configuration for arena 0");
		}
		bool currentlyMerged = currentArena.mergeNextArena;
		for (int i = 1; i < totalArenas; i++)
		{
			if (currentlyMerged) { mergedArenas.Add(i); }
			if (!_environmentManager.GetConfiguration(i, out currentArena))
			{
				Debug.LogError($"Critical error: Failed to load arena configuration for arena {i}");
			}
			currentlyMerged = currentArena.mergeNextArena;
		}
		return mergedArenas;
	}

	#region Arena Handling Methods

	/// <summary>
	/// Resets the arena by destroying existing objects and spawning new ones based on the current arena configuration.
	/// This is a custom implementation of the ResetAcademy method from the MLAgents library. It is called by the TrainingAgent when it resets.
	/// </summary>
	public void ResetArena()
	{
		Debug.Log("Resetting Arena");

		CleanUpSpawnedObjects();

		SetNextArenaID();

		UpdateActiveArenaToCurrentArenaID();
	}

	public void LoadNextArena()
	{
		// TrainingArena must have reset() called at first to initialise arenaID
		if (isFirstArenaReset)
		{
			throw new InvalidOperationException("LoadNextArena called before first reset");
		}

		Debug.Log($"Loading next arena. Previous: {arenaID}, next: {arenaID + 1}");
		CleanUpSpawnedObjects();

		arenaID += 1;
		// We need an arena to sequentially follow the current one to LoadNextArena
		if (!_environmentManager.GetConfiguration(arenaID, out _))
		{
			// TODO: This is the error hit if mergeNextArena is put in the final arena. Add some validation to move this from a runtime error
			throw new InvalidOperationException($"Tried to LoadNextArena but arena {arenaID} did not exist");
		}

		UpdateActiveArenaToCurrentArenaID();
	}

	private void CleanUpSpawnedObjects()
	{
		foreach (GameObject holder in transform.FindChildrenWithTag("spawnedObjects"))
		{
			holder.SetActive(false);
			Destroy(holder);
		}
	}

	private void SetNextArenaID()
	{
		int totalArenas = _environmentManager.getArenaCount();
		bool randomizeArenas = _environmentManager.GetRandomizeArenasStatus();

		if (isFirstArenaReset)
		{
			isFirstArenaReset = false;
			arenaID = randomizeArenas ? ChooseRandomArenaID(totalArenas) : 0;
		}
		else
		{
			if (randomizeArenas)
			{
				arenaID = ChooseRandomArenaID(totalArenas);
			}
			else
			{
				// If the next arena is merged, sequentially search for the next unmerged one
				ArenaConfiguration preceedingArena = _arenaConfiguration;
				arenaID = (arenaID + 1) % totalArenas;
				while (preceedingArena.mergeNextArena)
				{
					if (!_environmentManager.GetConfiguration(arenaID, out preceedingArena))
					{
						Debug.LogError($"Critical error: Failed to load arena configuration for arena {arenaID}");
						return;
					}
					arenaID = (arenaID + 1) % totalArenas;
				}
			}
		}
	}

	private int ChooseRandomArenaID(int totalArenas)
	{
		// Populate the list of merged arenas if needed
		if (_mergedArenas == null){ _mergedArenas = GetMergedArenas(); }

		playedArenas.Add(arenaID);
		if (playedArenas.Count >= totalArenas)
		{
			playedArenas = new List<int> { arenaID };
		}

		var availableArenas = Enumerable.Range(0, totalArenas).Except(playedArenas).Except(_mergedArenas).ToList();
		return availableArenas[Random.Range(0, availableArenas.Count)];

	}

	private void UpdateActiveArenaToCurrentArenaID() {
		// Try to load the new configuration, throwing if it fails
		ArenaConfiguration newConfiguration;
		if (!_environmentManager.GetConfiguration(arenaID, out newConfiguration))
		{
			Debug.LogError($"Critical error: Failed to load arena configuration for arena {arenaID}");
			return;
		}

		// Apply the new arena configuration
		Debug.Log("Updating Arena Configuration");
		_arenaConfiguration = newConfiguration;
		_agent.showNotification = ArenasConfigurations.Instance.showNotification;
		_arenaConfiguration.SetGameObject(prefabs.GetList());
		_builder.Spawnables = _arenaConfiguration.spawnables;
		_arenaConfiguration.toUpdate = false;
		_agent.MaxStep = 0;
		_agent.timeLimit = _arenaConfiguration.TimeLimit * _agentDecisionInterval;
		_builder.Build();
		_arenaConfiguration.lightsSwitch.Reset();

		if (_arenaConfiguration.randomSeed != 0)
		{
			Random.InitState(_arenaConfiguration.randomSeed);
		}
		Debug.Log($"TimeLimit set to: {_arenaConfiguration.TimeLimit}");

		// Destroy all spawned rewards in the arena.
		foreach (var reward in spawnedRewards)
		{
			Destroy(reward);
		}
		spawnedRewards.Clear();

		// Notify Arena Change
		_environmentManager.TriggerArenaChangeEvent(arenaID, _environmentManager.GetTotalArenas());
	}
	
	#endregion

	#region Other Methods

	/// <summary>
	/// Updates the light status in the arena based on the current step count.
	/// </summary>
	public void UpdateLigthStatus()
	{
		int stepCount = _agent.StepCount;
		bool newLight = _arenaConfiguration.lightsSwitch.LightStatus(
			stepCount,
			_agentDecisionInterval
		);
		if (newLight != _lightStatus)
		{
			_lightStatus = newLight;
			foreach (Fade fade in _fades)
			{
				fade.StartFade();
			}
		}
	}

	/// <summary>
	/// Returns the total number of spawned objects in the arena.
	/// </summary>
	public int GetTotalSpawnedObjects()
	{
		Debug.Log("Total spawned objects: " + spawnedObjectsHolder.transform.childCount);
		return spawnedObjectsHolder.transform.childCount;
	}

	/// <summary>
	/// Callback for when a reward is spawned in the arena.
	/// </summary>
	private void OnRewardSpawned(GameObject reward)
	{
		spawnedRewards.Add(reward);
	}

	#endregion
}
