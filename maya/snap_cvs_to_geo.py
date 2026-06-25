"""Snap selected NURBS curve CVs to the closest point on a target mesh.

Designed for hair-groom cleanup in Maya (tested in Maya 2023.3.4): the root
CVs of curves often float a little off the head geo, so this pulls the selected
CVs straight onto the surface.

It only ever touches the CVs you have selected -- nothing else on the curve is
moved -- so to fix just the first CV of each curve, select those CVs and run.

Usage
-----
1. Select the CVs you want to snap (e.g. the first CV of each hair curve).
2. Add the target geo to the selection (shift-select the head mesh last),
   OR pass the mesh name explicitly to the function.
3. Run:

    import snap_cvs_to_geo
    snap_cvs_to_geo.snap_cvs_to_geo()

   or, naming the mesh directly so you don't have to add it to the selection:

    snap_cvs_to_geo.snap_cvs_to_geo(mesh="head_GEO")

The whole operation is wrapped in a single undo chunk, so one Ctrl+Z reverts it.
"""

import maya.cmds as cmds
import maya.api.OpenMaya as om


def _resolve_mesh_fn(mesh):
    """Return an MFnMesh for ``mesh`` (a transform or mesh shape name)."""
    sel = om.MSelectionList()
    sel.add(mesh)
    dag = sel.getDagPath(0)
    # If a transform was given, walk down to its mesh shape.
    if dag.apiType() != om.MFn.kMesh:
        dag.extendToShape()
    if dag.apiType() != om.MFn.kMesh:
        raise RuntimeError("'{}' is not a polygon mesh.".format(mesh))
    return om.MFnMesh(dag)


def _find_target_mesh(non_cv_selection):
    """Pick the first polygon mesh out of the non-CV part of the selection."""
    for node in non_cv_selection:
        if not cmds.objExists(node):
            continue
        if cmds.nodeType(node) == "mesh":
            return node
        shapes = cmds.listRelatives(
            node, shapes=True, type="mesh", noIntermediate=True, fullPath=True
        )
        if shapes:
            return shapes[0]
    return None


def snap_cvs_to_geo(mesh=None):
    """Snap the selected curve CVs to the closest point on ``mesh``.

    Parameters
    ----------
    mesh : str, optional
        Name of the target geo (transform or mesh shape). If omitted, the
        target is taken from the current selection (anything selected that
        isn't a CV), so just shift-select the head mesh along with the CVs.

    Returns
    -------
    int
        The number of CVs that were moved.
    """
    selection = cmds.ls(selection=True, flatten=True, long=True) or []

    cvs = [item for item in selection if ".cv[" in item]
    if not cvs:
        raise RuntimeError(
            "No curve CVs selected. Select the CVs you want to snap, then run again."
        )

    if mesh is None:
        non_cv = [item for item in selection if ".cv[" not in item]
        mesh = _find_target_mesh(non_cv)
    if not mesh:
        raise RuntimeError(
            "No target mesh found. Add the head geo to your selection "
            "(shift-select it) or pass mesh=\"<name>\"."
        )

    mesh_fn = _resolve_mesh_fn(mesh)

    cmds.undoInfo(openChunk=True)
    try:
        for cv in cvs:
            src = cmds.pointPosition(cv, world=True)
            closest, _face_id = mesh_fn.getClosestPoint(
                om.MPoint(src[0], src[1], src[2]), om.MSpace.kWorld
            )
            cmds.xform(
                cv,
                worldSpace=True,
                translation=(closest.x, closest.y, closest.z),
            )
    finally:
        cmds.undoInfo(closeChunk=True)

    print("Snapped {} CV(s) onto '{}'.".format(len(cvs), mesh))
    return len(cvs)


if __name__ == "__main__":
    # Lets you select your CVs (+ the head geo) and just run the file.
    snap_cvs_to_geo()
