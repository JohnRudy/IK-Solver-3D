//  The MIT License (MIT)

// Copyright © 2023 John Rudy

// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
// and associated documentation files (the “Software”), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all copies or substantial
// portions of the Software.

// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
// THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
// OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.


// A modified version of Joar Engberg implementation of Fabrik.
// https://github.com/joaen/EasyIK

// Modifications: 
// - Single component for all ik chains
// - User set transform chains to get rid of GetChild(0) bug if > 1 children
// - Pole targets can now be used with chains with length of 2 rather than only 3
// - Only given root bone and chainlength -> automatic chain setup with warnings

// This is no gradient decent but works well for how simplistic it is.


using System;
using System.Linq;
using UnityEngine;


namespace JohnRudy.Controllers.IK {
    public class IKSolver3D : MonoBehaviour {
        // |<-------IKSegment3D------->|
        // |<---joint--->|<---joint--->|
        // |<root>  ->   |<chain>  ->  |<tip>
        // |Upper arm    | forearm     | hand

        /// <summary>
        /// The segment object that holds the entire chain information
        /// </summary>
        [Serializable]
        public class IKSegment3D {
            public string Name;                         // Name of the segment "left hand, right hand etc...
            public Transform IKTarget;                  // The tip follow target
            public Transform IKPole;                    // The pole chain bend weighting
            
            [Header ( "Manual setup" )]
            public Transform [ ] JointTransforms;       // The transforms in order in the chain/limb

            [Header ( "Automatic setup" )]
            public Transform Root;                      // Root of the limb/chain
            public int ChainLength;                     // How long the chain is

            [HideInInspector] public float fullLength;                  // Sum of each bone length 
            [HideInInspector] public float [ ] jointLength;             // Each "bone" length
            [HideInInspector] public Quaternion [ ] jointRotations;     // Each "bone" rotation
            [HideInInspector] public Vector3 [ ] jointPositions;        // Each "bone" position
            [HideInInspector] public Vector3 [ ] jointDirections;       // Each "bone" direction

            // Used to solve drifting problem
            [HideInInspector] public Vector3 startPosition;             // Which position the root should stay at
 
            // used to ADD target rotation to tip rather than set tip to target rotation
            [HideInInspector] public Quaternion tipStartRotation;           // Tips starting rotation to add to
            [HideInInspector] public Quaternion ikTargetStartRotation;      // IK Targets starting rotation to add from

            // Can be initialized with either the full chain and gotten the rest of the info through that
            public IKSegment3D ( string name , Transform [ ] jointTransforms , Transform ikTarget , Transform ikPole = null ) {
                this.Name = name;
                this.JointTransforms = jointTransforms;
                this.IKTarget = ikTarget;
                if ( ikPole != null ) this.IKPole = ikPole;
            }

            // Can be initialized with only the root and length of the chain and initialized through that. 
            public IKSegment3D ( string name , Transform root , int chainLength , Transform ikTarget , Transform ikPole = null ) {
                this.Name = name;
                this.Root = root;
                this.ChainLength = chainLength;
                this.IKTarget = ikTarget;
                if ( ikPole != null ) this.IKPole = ikPole;
            }
        }

        [Tooltip ( "Each bone chain and it's targets and poles" )]
        public IKSegment3D [ ] segments;        // Each segment of this IK rig

        [Header ( "Solver settings" )]
        [Tooltip ( "How close to the target should the tip of the segment be before accepting solved position" )]
        public float Tolerance = 0.05f;         // How near to the target is "close enough" 
        [Tooltip ( "How many times will we try to solve for this frame" )]
        public int Iterations = 10;             // How many times do we try to get to the target


        private void Start ( ) {
            if ( segments != null ) {
                InitializeJoints ( );
            }
        }

        private void Update ( ) {
            if ( segments != null ) {
                DoFabrik ( );
            }
        }


        /// <summary>
        /// Initializes the joints with initialized segment values from the segments array
        /// with either only Root and Chainlength given or a full transform array. 
        /// Root bone and chainlength will take precedence if given. 
        /// </summary>
        public void InitializeJoints ( ) {
            foreach ( IKSegment3D seg in segments ) {

                // The original way of getting the chain now with warnings
                if ( seg.Root != null ) {

                    // If a too small chainlength has been given
                    if ( seg.ChainLength < 2 ) {
                        Debug.LogWarning ( "Chainlength must be greater than 1. Please adjust " + seg.Name );
                        return;
                    }

                    // Notify the user if the joints will be re written because of root and joints setup at the sametime
                    if ( seg.JointTransforms != null ) {
                        Debug.LogWarning (
                            "IKSolver3D: Root and JointTransforms both given! Root will take precedence. Chain might not be correct. Either give the entire chain in JointTransforms for the segment without Root and chainlength - or use only Root with chainlength."
                        );
                    }

                    // Get the chain
                    Transform current = seg.Root;
                    seg.JointTransforms = new Transform [ seg.ChainLength ];
                    for ( int i = 0 ; i < seg.ChainLength ; i++ ) {
                        seg.JointTransforms [ i ] = current;

                        // If multiple childs and not the tip of the chain
                        if ( current.childCount > 1 && i != seg.ChainLength - 1 ) {
                            Debug.LogWarning ( "More than one child found for " + seg.Name + ". Please setup this 1limb manually" );
                            return;
                        }

                        current = current.GetChild ( 0 );
                    }
                }
            }

            // When a full JointTransform array is setup
            // Does the rest of the Segment setup
            foreach ( IKSegment3D seg in segments ) {
                seg.ChainLength = seg.JointTransforms.Length;

                seg.jointLength = new float [ seg.ChainLength ];
                seg.jointRotations = new Quaternion [ seg.ChainLength ];
                seg.jointPositions = new Vector3 [ seg.ChainLength ];
                seg.jointDirections = new Vector3 [ seg.ChainLength ];

                seg.ikTargetStartRotation = seg.IKTarget.rotation;
                seg.tipStartRotation = seg.JointTransforms [ seg.JointTransforms.Length - 1 ].rotation;

                for ( int i = 0 ; i < seg.ChainLength ; i++ ) {
                    Transform currentJoint = seg.JointTransforms [ i ];
                    if ( i != seg.ChainLength - 1 ) {
                        seg.jointLength [ i ] = ( currentJoint.position - seg.JointTransforms [ i + 1 ].position ).magnitude;
                        seg.fullLength += seg.jointLength [ i ];
                        seg.jointDirections [ i ] = seg.JointTransforms [ i + 1 ].position - currentJoint.position;
                        seg.jointRotations [ i ] = currentJoint.rotation;
                    }
                }
            }
        }


        /// <summary>
        /// Main loop that iterates over each segments chains to achieve IK behaviour. 
        /// </summary>
        private void DoFabrik ( ) {
            // Update current positions
            foreach ( IKSegment3D segment in segments ) {
                for ( int i = 0 ; i < segment.JointTransforms.Length ; i++ ) segment.jointPositions [ i ] = segment.JointTransforms [ i ].position;

                float distanceToTarget = ( segment.JointTransforms [ 0 ].position - segment.IKTarget.position ).magnitude;

                // Target is out of reach
                if ( distanceToTarget > segment.fullLength ) {
                    Vector3 direction = segment.IKTarget.position - segment.JointTransforms [ 0 ].position;
                    for ( int i = 1 ; i < segment.jointPositions.Length ; i++ ) {
                        // Place the joints in sequence in a line towards the target from root
                        segment.jointPositions [ i ] = segment.jointPositions [ i - 1 ] + direction.normalized * segment.jointLength [ i - 1 ];
                    }
                }

                // Target is in reach
                else {
                    distanceToTarget = ( segment.jointPositions [ segment.jointPositions.Length - 1 ] - segment.IKTarget.position ).magnitude;

                    // The jittery feel comes from this. 
                    // The "Tolerance" value has to be extremely low or 0 to be constantly tracking and following at the right spot.
                    // 0 value is not wanted because of the tip jitters otherwise
                    // The iterative nature also changes how the ik behaves on faster movements
                    int currentIteration = 0;
                    while ( distanceToTarget > Tolerance ) {
                        segment.startPosition = segment.jointPositions [ 0 ];
                        Fabrik ( segment );
                        currentIteration++;
                        if ( currentIteration >= Iterations ) break;
                    }
                }

                if ( segment.IKPole != null && segment.JointTransforms.Length < 4 )
                    ApplyPole ( segment );

                // Apply new rotations to the transforms. 
                for ( int i = 0 ; i < segment.jointPositions.Length - 1 ; i++ ) {
                    segment.JointTransforms [ i ].position = segment.jointPositions [ i ];
                    Quaternion rotation = Quaternion.FromToRotation (
                        segment.jointDirections [ i ] ,
                        segment.jointPositions [ i + 1 ] - segment.jointPositions [ i ]
                    );
                    segment.JointTransforms [ i ].rotation = rotation * segment.jointRotations [ i ];
                }

                // Applying an offset addition to the tip so there's no need to add starting rotatinos to target
                Quaternion offset = segment.tipStartRotation * Quaternion.Inverse ( segment.ikTargetStartRotation );
                segment.JointTransforms.Last ( ).rotation = segment.IKTarget.rotation * offset;
            }
        }


        /// <summary>
        /// Main positional and rotational calculations for following the target of each segment. 
        /// </summary>
        /// <param name="segment"> Current segment to move</param>
        private void Fabrik ( IKSegment3D segment ) {

            // Reverse order
            for ( int i = segment.jointPositions.Length - 1 ; i >= 0 ; i -= 1 ) {

                // First place the tip joint on the target, following the chain down to their respective child positions.
                if ( i == segment.jointPositions.Length - 1 ) segment.jointPositions [ i ] = segment.IKTarget.position;
                else segment.jointPositions [ i ] = segment.jointPositions [ i + 1 ] + ( segment.jointPositions [ i ] - segment.jointPositions [ i + 1 ] ).normalized * segment.jointLength [ i ];
            }

            // In order
            for ( int i = 0 ; i < segment.jointPositions.Length ; i += 1 ) {

                // Place the root back on it's original position on the rig and then bring back the joints to the correct place along the chain.
                if ( i == 0 ) segment.jointPositions [ i ] = segment.startPosition;
                else segment.jointPositions [ i ] = segment.jointPositions [ i - 1 ] + ( segment.jointPositions [ i ] - segment.jointPositions [ i - 1 ] ).normalized * segment.jointLength [ i - 1 ];
            }
        }


        /// <summary>
        /// Applies a weighting factor to the chain where the chain bends towards the pole target before IK target
        /// </summary>
        /// <param name="segment">Current segment to move</param>
        private void ApplyPole ( IKSegment3D segment ) {
            // TODO: 
            // - Add a "pole forward axis" to the segment class to get the wanted direction from
            // - Make the pole target affect longer chains than 3 with a curve like positions
            
            // |0----------|1----------|2
            // joint 1 position is affected by poles

            // A fictisious point to get 2 chain length bones have a pole target
            // The axis Vector3.up has, at least to this point, been the one that points towards the next bone in chain with rigging
            Vector3 fictisious = segment.jointPositions [ 1 ] + ( segment.JointTransforms [ 1 ].TransformDirection ( Vector3.up ) * segment.jointLength [ 1 ] );

            Vector3 axis = ( fictisious - segment.jointPositions [ 0 ] ).normalized;
            Vector3 poleDirection = ( segment.IKPole.position - segment.jointPositions [ 0 ] ).normalized;
            Vector3 jointDirection = ( segment.jointPositions [ 1 ] - segment.jointPositions [ 0 ] ).normalized;

            // Placing both directions on the same "plane" so to speak and calculating the angle of of difference on that plane
            Vector3.OrthoNormalize ( ref axis , ref poleDirection );
            Vector3.OrthoNormalize ( ref axis , ref jointDirection );

            // Get the new angle to rotate to 
            Quaternion angle = Quaternion.FromToRotation ( jointDirection , poleDirection );

            // Create a new Vector3 position by converting rotation angle to the direction + position of given chain
            segment.jointPositions [ 1 ] = angle * ( segment.jointPositions [ 1 ] - segment.jointPositions [ 0 ] ) + segment.jointPositions [ 0 ];
        }
    }
}