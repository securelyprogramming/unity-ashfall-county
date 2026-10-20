using UnityEngine;

namespace Mayo.Frontier.Presentation
{
    [System.Serializable] public sealed class FrontierPurchasedIdleBone
    {
        public string path;
        public AnimationCurve x,y,z,w;
        public Quaternion Rotation(float time) { return new Quaternion(x.Evaluate(time),y.Evaluate(time),z.Evaluate(time),w.Evaluate(time)).normalized; }
    }
    /// <summary>Presentation follows snapshot movement; purchased animation never moves the authoritative entity.</summary>
    public sealed class FrontierPurchasedCharacter : MonoBehaviour
    {
        public bool purchasedLocomotion, genericRig;
        public AnimationClip purchasedIdle;
        public FrontierPurchasedIdleBone[] purchasedIdleBones;
        public bool idleAbsolutePose;
        Transform[] idleTargets;
        Quaternion[] idleRest, idleFirst;
        public int PurchasedIdleSamples { get; private set; }
        public float HeadHeight { get { return Head == null ? 0 : Vector3.Dot(Head.position-transform.position,transform.up); } }
        float idleTime;
        Animator animator;
        Transform leftUpperArm, rightUpperArm, leftThigh, rightThigh;
        Quaternion leftArmRest, rightArmRest, leftLegRest, rightLegRest;
        public Transform Head { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform RightHand { get; private set; }
        bool ready;
        public void Initialize()
        {
            if (ready) return;
            ready = true;
            animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            if (genericRig)
            {
                Head = Joint("Neck1"); LeftHand = Joint("HandL1"); RightHand = Joint("HandR1");
                leftUpperArm = Joint("ArmL1"); rightUpperArm = Joint("ArmR1"); leftThigh = Joint("LegL1"); rightThigh = Joint("LegR1");
                Align(leftUpperArm, Joint("ArmL2"), transform.forward - transform.up * .15f);
                Align(rightUpperArm, Joint("ArmR2"), transform.forward - transform.up * .15f);
            }
            else
            {
                if (!animator.isHuman) return;
                Head = animator.GetBoneTransform(HumanBodyBones.Head);
                LeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (!purchasedLocomotion)
                {
                    var leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    var rightElbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    Align(leftUpperArm, leftElbow, -transform.up); Align(leftElbow, LeftHand, -transform.up);
                    Align(rightUpperArm, rightElbow, -transform.up); Align(rightElbow, RightHand, -transform.up);
                }
            }
            if (purchasedIdle != null)
            {
                int count = purchasedIdleBones.Length; idleTargets = new Transform[count]; idleRest = new Quaternion[count]; idleFirst = new Quaternion[count];
                for (int i=0;i<count;i++) { idleTargets[i]=animator.transform.Find(purchasedIdleBones[i].path); idleRest[i]=idleTargets[i].localRotation; idleFirst[i]=purchasedIdleBones[i].Rotation(0); }
                SamplePurchasedIdle(0);
            }
            if (leftUpperArm != null) leftArmRest = leftUpperArm.localRotation;
            if (rightUpperArm != null) rightArmRest = rightUpperArm.localRotation;
            if (leftThigh != null) leftLegRest = leftThigh.localRotation;
            if (rightThigh != null) rightLegRest = rightThigh.localRotation;
            if (!purchasedLocomotion) animator.enabled = false;
        }
        void SamplePurchasedIdle(float time)
        {
            for(int i=0;i<idleTargets.Length;i++)
            {
                Quaternion rotation = purchasedIdleBones[i].Rotation(time);
                // The Sci-Fi preview uses a different animation reference pose. Preserve
                // imported bind orientation and apply its artist-authored rotational deltas.
                idleTargets[i].localRotation = idleAbsolutePose ? rotation : idleRest[i]*Quaternion.Inverse(idleFirst[i])*rotation;
            }
            PurchasedIdleSamples++;
        }
        Transform Joint(string suffix)
        {
            foreach (var joint in GetComponentsInChildren<Transform>(true))
                if (joint.name.EndsWith(suffix, System.StringComparison.Ordinal)) return joint;
            return null;
        }
        static void Align(Transform joint, Transform child, Vector3 direction)
        {
            if (joint == null || child == null || (child.position-joint.position).sqrMagnitude < .00001f) return;
            joint.rotation = Quaternion.FromToRotation(child.position-joint.position, direction) * joint.rotation;
        }
        void Rotate(Transform joint, Quaternion rest, float angle)
        {
            if (joint == null) return;
            var parent = joint.parent == null ? Quaternion.identity : joint.parent.rotation;
            joint.localRotation = Quaternion.Inverse(parent) * Quaternion.AngleAxis(angle, transform.right) * parent * rest;
        }
        public void Pose(float movement, float phase, bool active, float attack)
        {
            if (animator == null || !genericRig && !animator.isHuman) return;
            if (purchasedLocomotion)
            {
                animator.speed = active ? 1 : 0;
                animator.SetFloat("Speed", active ? movement : 0);
                return;
            }
            // Human locomotion remains the existing snapshot-driven gait until a compatible
            // human animation set is restored. Goblin clips are reserved for actual goblins.
            if (purchasedIdle != null)
            {
                if (active) idleTime += Time.deltaTime;
                SamplePurchasedIdle(movement < .05f ? Mathf.Repeat(idleTime,purchasedIdle.length) : 0);
                if (movement < .05f && attack <= 0) return;
            }
            float gait = active ? Mathf.Sin(phase) * Mathf.Clamp01(movement / 1.5f) : 0;
            Rotate(leftThigh,leftLegRest,gait*25); Rotate(rightThigh,rightLegRest,-gait*25);
            Rotate(leftUpperArm,leftArmRest,-gait*18); Rotate(rightUpperArm,rightArmRest,gait*18-attack*75);
        }
    }
}
