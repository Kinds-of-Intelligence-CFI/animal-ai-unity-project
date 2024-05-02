﻿using System.Linq;
using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using PrefabInterface;
using Unity.MLAgents.Sensors;
using YAMLDefs;
using System.IO;

/// <summary>
/// The TrainingAgent class is a subclass of the Agent class in the ML-Agents library.
/// It is used to define the behaviour of the agent in the training environment.
/// Actions are currently discrete. 2 branches of 0,1,2, 0,1,2 for forward and rotate respectively.
/// </summary>
public class TrainingAgent : Agent, IPrefab
{
	[Header("Agent Settings")]
	public float speed = 25f;
	public float quickStopRatio = 0.9f;
	public float rotationSpeed = 100f;
	public float rotationAngle = 0.25f;

	private int lastActionForward = 0;
	private int lastActionRotate = 0;

	[Header("Agent State / Other Variables")]
	[HideInInspector]
	public int numberOfGoalsCollected = 0;

	[HideInInspector]
	public ProgressBar progBar;
	private Rigidbody _rigidBody;
	private bool _isGrounded;
	private ContactPoint _lastContactPoint;

	[Header("Agent Rewards & Score")]
	private float _rewardPerStep;
	private float _previousScore = 0;
	private float _currentScore = 0;

	[Header("Agent Health")]
	public float health = 100f;
	private float _maxHealth = 100f;

	[Header("Agent Freeze & Countdown")]
	public float timeLimit = 0f;
	private float _nextUpdateHealth = 0f;
	private float _freezeDelay = 0f;
	private bool _isFrozen = false;

	private bool _nextUpdateCompleteArena = false;

	[Header("Agent Notification")]
	public bool showNotification = false;
	private TrainingArena _arena;
	private bool _isCountdownActive = false;

	[Header("CSV Logging")]
	public string csvFilePath = "Observations.csv";
	private StreamWriter writer;
	private bool headerWritten = false;

	public override void Initialize()
	{
		_arena = GetComponentInParent<TrainingArena>();
		_rigidBody = GetComponent<Rigidbody>();
		_rewardPerStep = timeLimit > 0 ? -1f / timeLimit : 0;
		progBar = GameObject.Find("UI ProgressBar").GetComponent<ProgressBar>();
		progBar.AssignAgent(this);
		health = _maxHealth;

		// Base path for the logs to be stored
		string basePath;

		if (Application.isEditor)
		{
			// The root directory is the parent of the Assets folder
			basePath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
		}
		else
		{
			// Important! For builds, use the parent of the directory where the executable resides
			basePath = Path.GetDirectoryName(Application.dataPath);
		}

		// Folder for the CSV logs
		string directoryPath = Path.Combine(basePath, "observationLogs");

		// Simple check to see if the directory exists, if not create it
		if (!Directory.Exists(directoryPath))
		{
			Directory.CreateDirectory(directoryPath);
		}

		// Full path for the CSV file
		// TODO: change the file name to something more descriptive
		csvFilePath = Path.Combine(directoryPath, "Observations.csv");

		writer = new StreamWriter(csvFilePath, true);
		if (!File.Exists(csvFilePath) || new FileInfo(csvFilePath).Length == 0)
		{
			// Attribute headers for the CSV file --> can be changed as needed
			writer.WriteLine("Episode,Step,Health,XVelocity,YVelocity,ZVelocity,XPosition,YPosition,ZPosition,ActionForward,ActionRotate");
			headerWritten = true;
		}
	}

	private void LogToCSV(Vector3 velocity, Vector3 position, int lastActionForward, int lastActionRotate)
	{
		writer.WriteLine($"{Academy.Instance.EpisodeCount},{StepCount},{health},{velocity.x},{velocity.y},{velocity.z},{position.x},{position.y},{position.z},{lastActionForward},{lastActionRotate}");
		writer.Flush();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		if (writer != null)
		{
			writer.Flush();
			writer.Close();
		}
	}

	public float GetPreviousScore()
	{
		return _previousScore;
	}

	public float GetFreezeDelay()
	{
		return _freezeDelay;
	}

	public void SetFreezeDelay(float v)
	{
		_freezeDelay = Mathf.Clamp(v, 0f, v);
		if (v != 0f && !_isCountdownActive)
		{
			Debug.Log(
				"Starting coroutine unfreezeCountdown() with wait seconds == " + GetFreezeDelay()
			);
			StartCoroutine(unfreezeCountdown());
		}
	}

	public bool IsFrozen()
	{
		return _freezeDelay > 0f || _isFrozen;
	}

	public void FreezeAgent(bool freeze)
	{
		_isFrozen = freeze;
		if (_isFrozen)
		{
			_rigidBody.velocity = Vector3.zero;
			_rigidBody.angularVelocity = Vector3.zero;
		}
	}

	private IEnumerator unfreezeCountdown()
	{
		_isCountdownActive = true;
		yield return new WaitForSeconds(GetFreezeDelay());

		Debug.Log("unfreezing!");
		SetFreezeDelay(0f);
		_isCountdownActive = false;
	}

	public override void CollectObservations(VectorSensor sensor)
	{
		sensor.AddObservation(health);
		Vector3 localVel = transform.InverseTransformDirection(_rigidBody.velocity);
		sensor.AddObservation(localVel);
		Vector3 localPos = transform.position;
		sensor.AddObservation(localPos);
		LogToCSV(localVel, localPos, lastActionForward, lastActionRotate);
	}

	public override void OnActionReceived(ActionBuffers action)
	{
		lastActionForward = Mathf.FloorToInt(action.DiscreteActions[0]);
		lastActionRotate = Mathf.FloorToInt(action.DiscreteActions[1]);
		if (!IsFrozen())
		{
			MoveAgent(lastActionForward, lastActionRotate);
		}

		Vector3 localVel = transform.InverseTransformDirection(_rigidBody.velocity);
		Vector3 localPos = transform.position;
		LogToCSV(localVel, localPos, lastActionForward, lastActionRotate);
		UpdateHealth(_rewardPerStep);
	}


	private void MoveAgent(int actionForward, int actionRotate)
	{
		if (IsFrozen())
		{
			// If the agent is frozen, stop all movement and rotation
			_rigidBody.velocity = Vector3.zero;
			_rigidBody.angularVelocity = Vector3.zero;
			return;
		}

		Vector3 directionToGo = Vector3.zero;
		Vector3 rotateDirection = Vector3.zero;
		Vector3 quickStop = Vector3.zero;

		if (_isGrounded)
		{
			switch (actionForward)
			{
				case 1:
					directionToGo = transform.forward * 1f;
					break;
				case 2:
					directionToGo = transform.forward * -1f;
					break;
				case 0: // Slow down faster than drag with no input
					quickStop = _rigidBody.velocity * quickStopRatio;
					_rigidBody.velocity = quickStop;
					break;
			}
		}

		switch (actionRotate)
		{
			case 1:
				rotateDirection = transform.up * 1f;
				break;
			case 2:
				rotateDirection = transform.up * -1f;
				break;
		}

		transform.Rotate(rotateDirection, Time.fixedDeltaTime * rotationSpeed);
		_rigidBody.AddForce(
			directionToGo.normalized * speed * 100f * Time.fixedDeltaTime,
			ForceMode.Acceleration
		);
	}

	public override void Heuristic(in ActionBuffers actionsOut)
	{
		var discreteActionsOut = actionsOut.DiscreteActions;
		discreteActionsOut[0] = 0;
		discreteActionsOut[1] = 0;
		if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
		{
			discreteActionsOut[0] = 1;
		}
		if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
		{
			discreteActionsOut[0] = 2;
		}
		if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
		{
			discreteActionsOut[1] = 1;
		}
		if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
		{
			discreteActionsOut[1] = 2;
		}
	}

	public void UpdateHealthNextStep(float updateAmount, bool andCompleteArena = false)
	{
		/// <summary>
		/// ML-Agents doesn't guarantee behaviour if an episode ends outside of OnActionReceived
		/// Therefore we queue any health updates to happen on the next action step.
		/// </summary>
		_nextUpdateHealth += updateAmount;
		if (andCompleteArena)
		{
			_nextUpdateCompleteArena = true;
		}
	}

	public void UpdateHealth(float updateAmount, bool andCompleteArena = false)
	{
		if (NotificationManager.Instance == null && showNotification == true)
		{
			Debug.LogError("NotificationManager instance is not set.");
			return;
		}
		/// <summary>
		/// Update the health of the agent and reset any queued updates
		/// If health reaches 0 or the episode is queued to end then call EndEpisode().
		/// </summary>
		if (!IsFrozen())
		{
			health += 100 * updateAmount;
			health += 100 * _nextUpdateHealth;
			_nextUpdateHealth = 0;
			AddReward(updateAmount);
		}
		_currentScore = GetCumulativeReward();
		if (health > _maxHealth)
		{
			health = _maxHealth;
		}
		else if (health <= 0)
		{
			health = 0;
			if (showNotification)
			{
				NotificationManager.Instance.ShowFailureNotification();
			}
			StartCoroutine(EndEpisodeAfterDelay());
			return;
		}
		if (andCompleteArena || _nextUpdateCompleteArena)
		{
			_nextUpdateCompleteArena = false;
			float cumulativeReward = this.GetCumulativeReward();

			if (cumulativeReward >= Arena.CurrentPassMark)
			{
				// If passed and the next arena is merged load that without ending the episode
				if (_arena.mergeNextArena)
				{
					_arena.LoadNextArena();
					return;
				}
				if (showNotification)
				{
					NotificationManager.Instance.ShowSuccessNotification();
				}
			}
			else
			{
				if (showNotification)
				{
					NotificationManager.Instance.ShowFailureNotification();
				}
			}
			StartCoroutine(EndEpisodeAfterDelay());
		}
	}

	public void AddExtraReward(float rewardFactor)
	{
		UpdateHealth(Math.Min(rewardFactor * _rewardPerStep, -0.001f));
	}

	IEnumerator EndEpisodeAfterDelay()
	{
		if (!showNotification)
		{
			EndEpisode();
			yield break;
		}

		yield return new WaitForSeconds(2.5f);
		NotificationManager.Instance.HideNotification();
		EndEpisode();
	}

	public override void OnEpisodeBegin()
	{
		writer.WriteLine($"\nNew Episode,{Academy.Instance.EpisodeCount},,,,,,,,");
		writer.Flush();
		EpisodeDebugLog();

		StopCoroutine("unfreezeCountdown");
		_previousScore = _currentScore;
		numberOfGoalsCollected = 0;
		_arena.ResetArena();
		_rewardPerStep = timeLimit > 0 ? -1f / timeLimit : 0;
		_isGrounded = false;
		health = _maxHealth;

		SetFreezeDelay(GetFreezeDelay());
	}

	private void EpisodeDebugLog()
	{
		Debug.Log("Episode Begin");
		Debug.Log($"Value of showNotification: {showNotification}");
		Debug.Log("Current Pass Mark: " + Arena.CurrentPassMark);
		Debug.Log("Number of Goals Collected: " + numberOfGoalsCollected);
	}

	void OnCollisionEnter(Collision collision)
	{
		foreach (ContactPoint contact in collision.contacts)
		{
			if (contact.normal.y > 0)
			{
				_isGrounded = true;
			}
		}
		_lastContactPoint = collision.contacts.Last();
	}

	void OnCollisionStay(Collision collision)
	{
		foreach (ContactPoint contact in collision.contacts)
		{
			if (contact.normal.y > 0)
			{
				_isGrounded = true;
			}
		}
		_lastContactPoint = collision.contacts.Last();
	}

	void OnCollisionExit(Collision collision)
	{
		if (_lastContactPoint.normal.y > 0)
		{
			_isGrounded = false;
		}
	}

	//******************************
	//PREFAB INTERFACE FOR THE AGENT
	//******************************
	public void SetColor(Vector3 color) { }

	public void SetSize(Vector3 scale) { }

	/// <summary>
	/// Returns a random position within the range for the object.
	/// </summary>
	public virtual Vector3 GetPosition(
		Vector3 position,
		Vector3 boundingBox,
		float rangeX,
		float rangeZ
	)
	{
		float xBound = boundingBox.x;
		float zBound = boundingBox.z;
		float xOut =
			position.x < 0
				? Random.Range(xBound, rangeX - xBound)
				: Math.Max(0, Math.Min(position.x, rangeX));
		float yOut = Math.Max(position.y, 0) + transform.localScale.y / 2 + 0.01f;
		float zOut =
			position.z < 0
				? Random.Range(zBound, rangeZ - zBound)
				: Math.Max(0, Math.Min(position.z, rangeZ));

		return new Vector3(xOut, yOut, zOut);
	}

	///<summary>
	/// If rotationY set to < 0 change to random rotation.
	///</summary>
	public virtual Vector3 GetRotation(float rotationY)
	{
		return new Vector3(0, rotationY < 0 ? Random.Range(0f, 360f) : rotationY, 0);
	}
}
