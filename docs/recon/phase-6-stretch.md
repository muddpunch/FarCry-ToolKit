# Phase 6: stretch recon and verified scope

**Status: complete.** The gated recon produced a safe supported subset. Verified XBG geometry and material
references are implemented; unverified specialized editors are explicitly rejected instead of guessing layouts.

## Meshes

The FC5 XBG reader validates chunk bounds and decodes all declared LODs, multi-buffer positions, normals,
UVs, triangle sections, LOD distances, and LTMR material slot references. Unsupported layouts fail closed.
The WPF viewer provides orbit, pan, zoom, fit-to-view, LOD switching, and keyboard control.

`XbgFbxExporter` emits deterministic FBX 7.4 ASCII containing every decoded LOD and section, vertex
positions, normals, flipped-V texture coordinates, material bindings, source material paths, and LOD
distance metadata. The CLI exposes `mesh export-fbx`; the viewer exports atomically without overwriting.
Skinning and skeletons are not exported because the current clean-room decoder has no verified bone map.

## Materials

LTMR slot names and paths are exposed in the viewer and preserved in FBX material metadata. No FC5 XBM
parameter editor is offered: public evidence currently confirms slot-level support but not a stable clean-room
FC5 material parameter schema suitable for safe mutation.

## Weather and time of day

No verified FC5 weather/time-of-day curve identity, binary layout, or semantic schema was established.
The existing schema-gated FCB workspace can edit a future confirmed schema without archive-layer changes,
but a specialized curve editor would currently invent field meanings and therefore remains intentionally
unsupported. This is the negative result of the required recon gate, not an unfinished implementation.

## Acceptance

- Synthetic multi-LOD XBG fixtures cover geometry, padding, materials, invalid indices, and FBX output.
- FBX export validates all attributes and indices before writing any bytes.
- CLI and WPF outputs use new-file atomic publication and never overwrite a destination.
- Unsupported XBG, skinning, material mutation, and curve layouts fail explicitly.

## External evidence

- https://github.com/Quiet-Joker/Dunia-Engine-XBG-Blender-Importer

The external project is behavioral evidence only; no source or data was copied.
