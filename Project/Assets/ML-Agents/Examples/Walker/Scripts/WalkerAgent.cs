using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using System.Diagnostics.Eventing.Reader;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgentsExamples;
using Unity.MLAgents.Sensors;
using BodyPart = Unity.MLAgentsExamples.BodyPart;
using Random = UnityEngine.Random;

public class WalkerAgent : Agent
{
    [Header("Walk Speed")]
    // [Range(0.1f, 10)]
    [Range(1, 9)]
    [SerializeField]
    //The walking speed to try and achieve
    private float m_TargetWalkingSpeed = 9;

    public float MTargetWalkingSpeed // property
    {
        get { return m_TargetWalkingSpeed; }
        set { m_TargetWalkingSpeed = Mathf.Clamp(value, 0, m_maxWalkingSpeed); }
    }
    private List<float> speedOptions = new List<float>() { 0.00001f, 3, 5, 7, 9 };

    // const float m_maxWalkingSpeed = 10; //The max walking speed
    const float m_maxWalkingSpeed = 9; //The max walking speed

    //Should the agent sample a new goal velocity each episode?
    //If true, walkSpeed will be randomly set between zero and m_maxWalkingSpeed in OnEpisodeBegin()
    //If false, the goal velocity will be walkingSpeed
    public bool randomizeWalkSpeedEachEpisode;

    //The direction an agent will walk during training.
    private Vector3 m_WorldDirToWalk = Vector3.right;

    [Header("Target To Walk Towards")] public Transform target; //Target the agent will walk towards during training.

    [Header("Body Parts")] public Transform hips;
    public Transform chest;
    public Transform spine;
    public Transform head;
    public Transform thighL;
    public Transform shinL;
    public Transform footL;
    public Transform thighR;
    public Transform shinR;
    public Transform footR;
    public Transform armL;
    public Transform forearmL;
    public Transform handL;
    public Transform armR;
    public Transform forearmR;
    public Transform handR;

    //This will be used as a stabilized model space reference point for observations
    //Because ragdolls can move erratically during training, using a stabilized reference transform improves learning
    OrientationCubeController m_OrientationCube;

    //The indicator graphic gameobject that points towards the target
    DirectionIndicator m_DirectionIndicator;
    JointDriveController m_JdController;
    EnvironmentParameters m_ResetParams;
    // private VectorSensor speedObservation;
    public float startingChestHeight;
    public float startingHipHeight;
    public float currentChestHeight;
    public float currentHipHeight;

    public override void Initialize()
    {
        m_OrientationCube = GetComponentInChildren<OrientationCubeController>();
        m_DirectionIndicator = GetComponentInChildren<DirectionIndicator>();

        //Setup each body part
        m_JdController = GetComponent<JointDriveController>();
        m_JdController.SetupBodyPart(hips);
        m_JdController.SetupBodyPart(chest);
        m_JdController.SetupBodyPart(spine);
        m_JdController.SetupBodyPart(head);
        m_JdController.SetupBodyPart(thighL);
        m_JdController.SetupBodyPart(shinL);
        m_JdController.SetupBodyPart(footL);
        m_JdController.SetupBodyPart(thighR);
        m_JdController.SetupBodyPart(shinR);
        m_JdController.SetupBodyPart(footR);
        m_JdController.SetupBodyPart(armL);
        m_JdController.SetupBodyPart(forearmL);
        m_JdController.SetupBodyPart(handL);
        m_JdController.SetupBodyPart(armR);
        m_JdController.SetupBodyPart(forearmR);
        m_JdController.SetupBodyPart(handR);

        m_ResetParams = Academy.Instance.EnvironmentParameters;
        startingChestHeight = GetDistToGround(chest);
        startingHipHeight = GetDistToGround(hips);
        // SetResetParameters();
    }

    public LayerMask groundLayer;
    float GetDistToGround(Transform t)
    {
        RaycastHit hit;
        var maxDist = 2f;
        Ray ray = new Ray(t.position, Vector3.down);
        if (Physics.Raycast(ray, out hit, maxDist, groundLayer))
        {
            return hit.distance / maxDist;
        }
        else
        {
            return 1f;
        }
    }

    private int randomSpeedChoiceIndex;
    /// <summary>
    /// Loop over body parts and reset them to initial conditions.
    /// </summary>
    public override void OnEpisodeBegin()
    {
        //Reset all of the body parts
        foreach (var bodyPart in m_JdController.bodyPartsDict.Values)
        {
            bodyPart.Reset(bodyPart);
            bodyPart.InitializeRandomJointSettings();

        }

        //Random start rotation to help generalize
        hips.rotation = Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0);



        UpdateOrientationObjects();

        randomSpeedChoiceIndex = Random.Range(0, speedOptions.Count);
        MTargetWalkingSpeed = (float)speedOptions[randomSpeedChoiceIndex];

        // //Set our goal walking speed
        // MTargetWalkingSpeed =
        //     randomizeWalkSpeedEachEpisode ? Random.Range(0, m_maxWalkingSpeed) : MTargetWalkingSpeed;

        // SetResetParameters();
        StartCoroutine(WaitingPeriod());

    }
    public bool canRequestDecision = false;

    IEnumerator WaitingPeriod()
    {
        canRequestDecision = false;
        yield return new WaitForSeconds(2);
        WaitForFixedUpdate wait = new WaitForFixedUpdate();
        //wait until the body rigidbody is not moving
        var bodyRB = m_JdController.bodyPartsDict[hips].rb;
        while (bodyRB.velocity.magnitude > 0.1f && bodyRB.angularVelocity.magnitude > 0.1f)
        {
            yield return wait;
        }
        canRequestDecision = true;
    }

    /// <summary>
    /// Add relevant information on each body part to observations.
    /// </summary>
    public void CollectObservationBodyPart(BodyPart bp, VectorSensor sensor)
    {
        //GROUND CHECK
        sensor.AddObservation(bp.groundContact.touchingGround); // Is this bp touching the ground

        //Get velocities in the context of our orientation cube's space
        //Note: You can get these velocities in world space as well but it may not train as well.
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.velocity));
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.angularVelocity));

        //Get position relative to hips in the context of our orientation cube's space
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.position - hips.position));

        if (bp.rb.transform != hips && bp.rb.transform != handL && bp.rb.transform != handR)
        {
            sensor.AddObservation(bp.rb.transform.localRotation);
            // sensor.AddObservation(bp.currentStrength / m_JdController.maxJointForceLimit);
        }


    }


    public float chestUpDot;
    public float hipsUpDot;
    public float shinLUpDot;
    public float shinRUpDot;
    public float legLUpDot;
    public float legRUpDot;
    /// <summary>
    /// Loop over body parts to add them to observation.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // if (speedObservation == null)
        // {
        //     // speedObservation = new VectorSensor(5, "speedObsv", ObservationType.GoalSignal);
        //     speedObservation = GetComponent<VectorSensorComponent>().GetSensor();
        // }
        //
        // speedObservation.AddOneHotObservation(randomSpeedChoiceIndex, speedOptions.Count);

        currentChestHeight = Mathf.Clamp01(GetDistToGround(chest) / startingChestHeight);
        currentHipHeight = Mathf.Clamp01(GetDistToGround(hips) / startingChestHeight);
        // currentHipHeight = GetDistToGround(hips);
        // currentChestHeight = GetDistToGround(chest);
        // currentHipHeight = GetDistToGround(hips);
        sensor.AddObservation(currentChestHeight);
        sensor.AddObservation(currentHipHeight);

        hipsUpDot = Mathf.Clamp01((Vector3.Dot(hips.up, Vector3.up) + 1) / 2);
        // chestUpDot = Mathf.Clamp01((Vector3.Dot(chest.up, Vector3.up) + 1) / 2);
        shinLUpDot = Mathf.Clamp01((Vector3.Dot(shinL.up, Vector3.up) + 1) / 2);
        shinRUpDot = Mathf.Clamp01((Vector3.Dot(shinR.up, Vector3.up) + 1) / 2);
        // legLUpDot = Mathf.Clamp01((Vector3.Dot(thighL.up, Vector3.up) + 1) / 2);
        // legRUpDot = Mathf.Clamp01((Vector3.Dot(thighR.up, Vector3.up) + 1) / 2);

        sensor.AddObservation(hipsUpDot);
        // sensor.AddObservation(chestUpDot);
        sensor.AddObservation(shinLUpDot);
        sensor.AddObservation(shinRUpDot);
        // sensor.AddObservation(legLUpDot);
        // sensor.AddObservation(legRUpDot);

        var headHeightDelta = head.position.y - ((footL.position.y + footR.position.y) / 2);
        sensor.AddObservation(headHeightDelta);


        var cubeForward = m_OrientationCube.transform.forward;

        //velocity we want to match
        var velGoal = cubeForward * MTargetWalkingSpeed;
        //ragdoll's avg vel
        var avgVel = GetAvgVelocity();

        //current ragdoll velocity. normalized
        sensor.AddObservation(Vector3.Distance(velGoal, avgVel));
        //avg body vel relative to cube
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(avgVel));
        //vel goal relative to cube
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(velGoal));

        //rotation deltas
        sensor.AddObservation(Quaternion.FromToRotation(hips.forward, cubeForward));
        sensor.AddObservation(Quaternion.FromToRotation(head.forward, cubeForward));

        //Position of target position relative to cube
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformPoint(target.transform.position));

        foreach (var bodyPart in m_JdController.bodyPartsList)
        {
            CollectObservationBodyPart(bodyPart, sensor);
        }
    }

    public float chestStandImpulseForce = 50;
    public bool addChestStandForce;
    public override void OnActionReceived(ActionBuffers actionBuffers)

    {
        var bpDict = m_JdController.bodyPartsDict;
        var i = -1;

        var continuousActions = actionBuffers.ContinuousActions;
        bpDict[chest].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);
        bpDict[spine].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);

        bpDict[thighL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[thighR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[shinL].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[shinR].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[footR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);
        bpDict[footL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);

        bpDict[armL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[armR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[forearmL].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[forearmR].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[head].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);


        //add chest impulse to help stand
        if (continuousActions[++i] > 0)
        {
            addChestStandForce = true;
            // bpDict[chest].rb.AddForce(chestStandImpulseForce * Vector3.up, ForceMode.Impulse);
        }
        // //update joint strength settings
        // bpDict[chest].SetJointStrength(continuousActions[++i]);
        // bpDict[spine].SetJointStrength(continuousActions[++i]);
        // bpDict[head].SetJointStrength(continuousActions[++i]);
        // bpDict[thighL].SetJointStrength(continuousActions[++i]);
        // bpDict[shinL].SetJointStrength(continuousActions[++i]);
        // bpDict[footL].SetJointStrength(continuousActions[++i]);
        // bpDict[thighR].SetJointStrength(continuousActions[++i]);
        // bpDict[shinR].SetJointStrength(continuousActions[++i]);
        // bpDict[footR].SetJointStrength(continuousActions[++i]);
        // bpDict[armL].SetJointStrength(continuousActions[++i]);
        // bpDict[forearmL].SetJointStrength(continuousActions[++i]);
        // bpDict[armR].SetJointStrength(continuousActions[++i]);
        // bpDict[forearmR].SetJointStrength(continuousActions[++i]);
    }

    //Update OrientationCube and DirectionIndicator
    void UpdateOrientationObjects()
    {
        m_WorldDirToWalk = target.position - hips.position;
        m_OrientationCube.UpdateOrientation(hips, target);
        if (m_DirectionIndicator)
        {
            m_DirectionIndicator.MatchOrientation(m_OrientationCube.transform);
        }
    }


    public float hipHeightRew;
    public float chestHeightRew;
    void AddStandingRewards()
    {
        // chestHeightRew = Mathf.Clamp01(currentChestHeight/startingChestHeight);
        // hipHeightRew = Mathf.Clamp01(currentHipHeight/startingHipHeight);

        // AddReward(chestHeightRew * hipHeightRew * hipsUpDot);
        // AddReward(currentChestHeight * currentHipHeight * hipsUpDot);
        AddReward(currentChestHeight * currentHipHeight * hipsUpDot * shinLUpDot * shinRUpDot);
    }



    public float headHeightRew;
    void AddRewards()
    {

        var cubeForward = m_OrientationCube.transform.forward;

        // Set reward for this step according to mixture of the following elements.
        // a. Match target speed
        //This reward will approach 1 if it matches perfectly and approach zero as it deviates
        var matchSpeedReward = GetMatchingVelocityReward(cubeForward * MTargetWalkingSpeed, GetAvgVelocity());

        // b. Rotation alignment with target direction.
        //This reward will approach 1 if it faces the target direction perfectly and approach zero as it deviates
        var charForward = (head.forward + hips.forward) / 2;
        charForward.y = 0;
        // var lookAtTargetReward = (Vector3.Dot(cubeForward, head.forward) + 1) * .5F;
        var lookAtTargetReward = (Vector3.Dot(cubeForward, charForward) + 1) * .5F;


        headHeightRew = head.position.y - ((footL.position.y + footR.position.y) / 2);
        var facingUpRew = hipsUpDot * chestUpDot * shinLUpDot * shinRUpDot * legRUpDot * legLUpDot;
        var rew = facingUpRew;
        // if (headHeightRew > 1.3f)
        // {
        //     rew *= lookAtTargetReward;
        //     if (lookAtTargetReward > .8f)
        //     {
        //         rew *= matchSpeedReward;
        //     }
        // }
        // AddReward(matchSpeedReward * lookAtTargetReward);
        // if (canRequestDecision)
        // {
        //height first
        // if()
        // AddReward(matchSpeedReward * lookAtTargetReward * headHeightRew);
        AddReward(rew);
        // }
    }

    void FixedUpdate()
    {
        UpdateOrientationObjects();
        // if (canRequestDecision)
        // {
        //     //height first
        //     // if()
        //     // AddReward(matchSpeedReward * lookAtTargetReward * headHeightRew);
        // }
        if (canRequestDecision && Academy.Instance.StepCount % 5 == 0)
        {
            if (addChestStandForce)
            {
                print("adding chest force");
                // AddReward(-.25f);
                addChestStandForce = false;
                m_JdController.bodyPartsDict[chest].rb.AddForce(chestStandImpulseForce * Vector3.up, ForceMode.Impulse);
            }
            // AddRewards();
            AddStandingRewards();
            RequestDecision();
        }
    }

    //Returns the average velocity of all of the body parts
    //Using the velocity of the hips only has shown to result in more erratic movement from the limbs, so...
    //...using the average helps prevent this erratic movement
    Vector3 GetAvgVelocity()
    {
        Vector3 velSum = Vector3.zero;

        //ALL RBS
        int numOfRb = 0;
        foreach (var item in m_JdController.bodyPartsList)
        {
            numOfRb++;
            velSum += item.rb.velocity;
        }

        var avgVel = velSum / numOfRb;
        return avgVel;
    }

    //normalized value of the difference in avg speed vs goal walking speed.
    public float GetMatchingVelocityReward(Vector3 velocityGoal, Vector3 actualVelocity)
    {
        //distance between our actual velocity and goal velocity
        var velDeltaMagnitude = Mathf.Clamp(Vector3.Distance(actualVelocity, velocityGoal), 0, MTargetWalkingSpeed);

        //return the value on a declining sigmoid shaped curve that decays from 1 to 0
        //This reward will approach 1 if it matches perfectly and approach zero as it deviates
        return Mathf.Pow(1 - Mathf.Pow(velDeltaMagnitude / MTargetWalkingSpeed, 2), 2);
    }

    /// <summary>
    /// Agent touched the target
    /// </summary>
    public void TouchedTarget()
    {
        AddReward(1f);
    }

    // public void SetTorsoMass()
    // {
    //     m_JdController.bodyPartsDict[chest].rb.mass = m_ResetParams.GetWithDefault("chest_mass", 8);
    //     m_JdController.bodyPartsDict[spine].rb.mass = m_ResetParams.GetWithDefault("spine_mass", 8);
    //     m_JdController.bodyPartsDict[hips].rb.mass = m_ResetParams.GetWithDefault("hip_mass", 8);
    // }
    //
    // public void SetResetParameters()
    // {
    //     SetTorsoMass();
    // }
}
