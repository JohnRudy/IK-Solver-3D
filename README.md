# IK-Solver-3D

A modified version of Joar Engberg implementation of Fabrik.
https://github.com/joaen/EasyIK

# Modifications

- Single component for all ik chains
- User set transform chains to get rid of GetChild(0) bug if > 1 children
- Pole targets can now be used with chains with length of 2 rather than only 3
- Only given root bone and chainlength -> automatic chain setup with warnings

# Usage

1. Place the component on a gameobject 
2. Create add segments to the "segments" array that are wanted
  - Optional: Name your segment. Not needed but for your own sanitys sake. 
3. Create a empty game object for IK Target (and IK Pole if so desired).
4. Choose either the "Manual Setup" or "Automatic Setup"
  - Manual Setup: add all transforms in the given "limb" in order into the segments "joint transforms" array.
  - Automatic Setup: Give the segment a "Root" transform (top level parent) and chain length on how many transforms to affect. 
5. Setup desired Tolerance and Iterations variables. 
  - Tolerance: How near to the target is close enough to stop the tip to (stops tip from jittering)
  - Iterations: How many times a frame do we calculate transform positions and rotations (How fast the tip catches the target)
  
 # Improvements to come
 
 - Make pole target affect an entire length of chain rather than just root and first child
 - Modify the ApplyPole method to take the given segments "bone axis forward" to bend. 
