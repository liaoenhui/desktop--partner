# Codex mode visual baseline (v2)

The desktop pet uses two visible forms only:

1. Daily form: the existing overworld / character-screen chibi.
2. Invincible Player form: star visor over the eyes, luminous blade wings, and luminous segmented tail.

The mechanical-wing battle-normal form is reference material for attachment points and silhouette only. It is not a separate runtime state.

Planned transition:

- neutral daily pose
- hand rises toward the visor
- cyan/magenta star visor flashes into place
- wing roots light up and the tail resolves from pixel fragments
- luminous blade wings fan outward
- hand lowers into the transformed idle pose

The animation should reverse cleanly when Codex work ends.

Current review assets:

- `invincible-player-reference-v2.png`: canonical transformed appearance.
- `transform-keyframes-v2.png`: 4 x 2 source keyframe sheet.
- `transform-v2/frame_00.png` through `frame_07.png`: padded transparent runtime frames.
- `transform-v2/sequence.json`: frame timing and state metadata.
- `transform-v2-preview.gif`: looping review preview.

The v2 transformation pose sequence is superseded by `transform-v3/sequence-spec.json`. The corrected choreography requires Silver Wolf's left hand (viewer-right) to present a game cartridge while her right hand (viewer-left) swipes the control strip on its glove before the star visor and light wings appear. V2 remains only as a visual reference for the final visor, wings, and tail.

The active transformation occupies about 760 ms; the final preview frame holds for 500 ms. Runtime easing and a short cyan/magenta flash can be layered between frames without introducing a third visible character state.
