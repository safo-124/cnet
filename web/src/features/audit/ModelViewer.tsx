import { useEffect, useRef, useState } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import { IfcAPI } from 'web-ifc'
import wasmUrl from 'web-ifc/web-ifc.wasm?url'
import { Loader2Icon, RotateCcwIcon } from 'lucide-react'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import type { AuditDevice } from '@/lib/api'
import { STATUS_INFO, type StatusTone } from '@/lib/status'

interface Props {
  file: File
  devices: AuditDevice[]
  /** Devices outside the current filter are drawn faded. */
  visibleIds: Set<string>
  selectedId: string | null
  onSelect: (id: string | null) => void
}

type LoadState = { kind: 'loading' } | { kind: 'ready'; meshes: number } | { kind: 'empty' } | { kind: 'error'; message: string }

const TONE_VARS: Record<StatusTone, string> = {
  success: '--chart-ok',
  warning: '--chart-fixable',
  danger: '--chart-designer',
}

/**
 * Draws the IFC model in the browser with web-ifc (WebAssembly) and three.js. Building elements are translucent;
 * audited devices are colored by result, and can be hovered and clicked. Nothing is sent to the server.
 */
export default function ModelViewer({ file, devices, visibleIds, selectedId, onSelect }: Props) {
  const container = useRef<HTMLDivElement>(null)
  const scene = useRef<SceneHandles | null>(null)
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [hovered, setHovered] = useState<{ id: string; x: number; y: number } | null>(null)
  const onSelectRef = useRef(onSelect)
  onSelectRef.current = onSelect
  const byId = new Map(devices.map((d) => [d.id, d]))

  // Build the scene once per file.
  useEffect(() => {
    const host = container.current
    if (!host) return
    let disposed = false
    const handles = createScene(host)
    scene.current = handles

    handles.onHover = (id, x, y) => setHovered(id ? { id, x, y } : null)
    handles.onClick = (id) => onSelectRef.current(id)

    loadModel(file, handles, new Map(devices.map((d) => [d.id, toneColor(STATUS_INFO[d.status].tone)])))
      .then((count) => {
        if (disposed) return
        setState(count === 0 ? { kind: 'empty' } : { kind: 'ready', meshes: count })
        handles.fit()
      })
      .catch((error: unknown) => !disposed && setState({ kind: 'error', message: String(error) }))

    return () => {
      disposed = true
      handles.dispose()
      scene.current = null
    }
    // The device list only changes together with the file.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [file])

  // Highlighting follows the filter and the selection without rebuilding the scene.
  useEffect(() => {
    scene.current?.highlight(visibleIds, selectedId)
  }, [visibleIds, selectedId, state])

  const hoveredDevice = hovered ? byId.get(hovered.id) : undefined

  return (
    <div className="relative h-[520px] overflow-hidden bg-muted/30">
      <div ref={container} className="absolute inset-0" />

      {state.kind !== 'ready' && (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center text-sm text-muted-foreground">
          {state.kind === 'loading' && (
            <>
              <Loader2Icon className="size-5 animate-spin" />
              Building the 3D view…
            </>
          )}
          {state.kind === 'empty' && <>This model has no 3D geometry, so there is nothing to draw.</>}
          {state.kind === 'error' && <>The 3D view could not be built: {state.message}</>}
        </div>
      )}

      {state.kind === 'ready' && (
        <>
          <div className="absolute top-3 left-3 flex flex-wrap items-center gap-x-4 gap-y-1 rounded-lg border bg-card/90 px-3 py-2 text-xs shadow-sm backdrop-blur">
            {(['success', 'warning', 'danger'] as const).map((tone) => (
              <span key={tone} className="flex items-center gap-1.5">
                <span className="size-2.5 rounded-sm" style={{ background: `var(${TONE_VARS[tone]})` }} />
                {tone === 'success' ? 'OK' : tone === 'warning' ? 'Fixable' : 'Needs designer'}
              </span>
            ))}
            <span className="hidden text-muted-foreground sm:inline">Drag to orbit · scroll to zoom · click a device</span>
          </div>
          <Button variant="outline" size="sm" className="absolute top-3 right-3 bg-card/90" onClick={() => scene.current?.fit()}>
            <RotateCcwIcon /> Reset view
          </Button>
        </>
      )}

      {hoveredDevice && hovered && (
        <div
          className="pointer-events-none absolute z-10 rounded-lg border bg-popover px-3 py-2 text-sm shadow-md"
          style={{ left: Math.min(hovered.x + 14, (container.current?.clientWidth ?? 0) - 240), top: hovered.y + 14, width: 226 }}
        >
          <div className="truncate font-medium">{hoveredDevice.name ?? '(unnamed)'}</div>
          <div className="mt-1 flex items-center justify-between gap-2">
            <span className="truncate text-xs text-muted-foreground">{hoveredDevice.model ?? 'No product'}</span>
            <StatusBadge status={hoveredDevice.status} />
          </div>
        </div>
      )}
    </div>
  )
}

// ---- three.js scene ---------------------------------------------------------------------------------------------

interface SceneHandles {
  root: THREE.Group
  devices: Map<string, THREE.Mesh[]>
  onHover: (id: string | null, x: number, y: number) => void
  onClick: (id: string | null) => void
  fit: () => void
  highlight: (visible: Set<string>, selected: string | null) => void
  dispose: () => void
}

function createScene(host: HTMLDivElement): SceneHandles {
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true })
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
  renderer.setSize(host.clientWidth, host.clientHeight)
  host.appendChild(renderer.domElement)

  const scene = new THREE.Scene()
  const camera = new THREE.PerspectiveCamera(45, host.clientWidth / host.clientHeight, 0.1, 2000)
  const controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true

  scene.add(new THREE.HemisphereLight(0xffffff, 0x8899aa, 2.2))
  const sun = new THREE.DirectionalLight(0xffffff, 1.6)
  sun.position.set(30, 50, 20)
  scene.add(sun)

  // No axis conversion needed: web-ifc's transformations already turn IFC's Z-up millimetres into Y-up metres.
  const root = new THREE.Group()
  scene.add(root)

  const handles: SceneHandles = {
    root,
    devices: new Map(),
    onHover: () => {},
    onClick: () => {},
    fit() {
      // Meshes use fixed matrices (matrixAutoUpdate = false); their world matrices are only refreshed at render
      // time, so refresh them now or the box is measured from raw, unplaced geometry.
      root.updateMatrixWorld(true)
      const box = new THREE.Box3().setFromObject(root)
      if (box.isEmpty()) return
      const center = box.getCenter(new THREE.Vector3())
      const size = box.getSize(new THREE.Vector3()).length()
      // Back off until the model fits the narrower of the two fields of view. A bounding-sphere fit is too
      // conservative for long, flat buildings, so use the plain tangent fit on the box diagonal.
      const verticalFov = THREE.MathUtils.degToRad(camera.fov)
      const fov = Math.min(verticalFov, 2 * Math.atan(Math.tan(verticalFov / 2) * camera.aspect))
      const distance = size / 2 / Math.tan(fov / 2)
      camera.position.copy(center).add(new THREE.Vector3(0.55, 0.5, 0.75).normalize().multiplyScalar(distance))
      camera.near = size / 200
      camera.far = size * 10
      camera.updateProjectionMatrix()
      controls.target.copy(center)
      controls.update()
    },
    highlight(visible, selected) {
      for (const [id, meshes] of handles.devices) {
        const faded = !visible.has(id) || (selected !== null && selected !== id)
        for (const mesh of meshes) {
          const material = mesh.material as THREE.MeshStandardMaterial
          material.opacity = faded ? 0.18 : 1
          material.transparent = faded
          material.emissiveIntensity = id === selected ? 0.6 : 0
        }
      }
    },
    dispose() {
      cancelAnimationFrame(frame)
      resize.disconnect()
      renderer.domElement.removeEventListener('pointermove', onPointerMove)
      renderer.domElement.removeEventListener('pointerdown', onPointerDown)
      renderer.domElement.removeEventListener('pointerup', onPointerUp)
      root.traverse((object) => {
        if (object instanceof THREE.Mesh) {
          object.geometry.dispose()
          ;(object.material as THREE.Material).dispose()
        }
      })
      controls.dispose()
      renderer.dispose()
      renderer.domElement.remove()
    },
  }

  // Picking: only devices are pickable, so a click on a wall doesn't hide the device behind it.
  const raycaster = new THREE.Raycaster()
  const pointer = new THREE.Vector2()
  const pick = (event: PointerEvent): string | null => {
    const rect = renderer.domElement.getBoundingClientRect()
    pointer.set(((event.clientX - rect.left) / rect.width) * 2 - 1, -((event.clientY - rect.top) / rect.height) * 2 + 1)
    raycaster.setFromCamera(pointer, camera)
    const hit = raycaster.intersectObjects([...handles.devices.values()].flat(), false)[0]
    return (hit?.object.userData.deviceId as string | undefined) ?? null
  }
  const onPointerMove = (event: PointerEvent) => {
    const id = pick(event)
    const rect = renderer.domElement.getBoundingClientRect()
    renderer.domElement.style.cursor = id ? 'pointer' : 'grab'
    handles.onHover(id, event.clientX - rect.left, event.clientY - rect.top)
  }
  // Treat it as a click only when the pointer didn't move, so orbiting doesn't change the selection.
  let downAt: { x: number; y: number } | null = null
  const onPointerDown = (event: PointerEvent) => (downAt = { x: event.clientX, y: event.clientY })
  const onPointerUp = (event: PointerEvent) => {
    if (downAt && Math.hypot(event.clientX - downAt.x, event.clientY - downAt.y) < 4)
      handles.onClick(pick(event))
    downAt = null
  }
  renderer.domElement.addEventListener('pointermove', onPointerMove)
  renderer.domElement.addEventListener('pointerdown', onPointerDown)
  renderer.domElement.addEventListener('pointerup', onPointerUp)

  const resize = new ResizeObserver(() => {
    camera.aspect = host.clientWidth / Math.max(1, host.clientHeight)
    camera.updateProjectionMatrix()
    renderer.setSize(host.clientWidth, host.clientHeight)
  })
  resize.observe(host)

  let frame = 0
  const render = () => {
    frame = requestAnimationFrame(render)
    controls.update()
    renderer.render(scene, camera)
  }
  render()

  return handles
}

/** Streams every mesh of the IFC file into the scene. Returns how many meshes were drawn. */
async function loadModel(file: File, handles: SceneHandles, deviceColors: Map<string, THREE.Color>): Promise<number> {
  const api = new IfcAPI()
  // Single-threaded: the multi-threaded build needs cross-origin isolation headers the site doesn't send.
  await api.Init(() => wasmUrl, true)
  const modelId = api.OpenModel(new Uint8Array(await file.arrayBuffer()))
  if (modelId < 0) throw new Error('web-ifc could not open the file.')

  const buildingMaterial = new THREE.MeshStandardMaterial({
    color: 0xb8c2d0,
    transparent: true,
    opacity: 0.16,
    depthWrite: false,
    side: THREE.DoubleSide,
  })
  let count = 0

  try {
    // Geometry comes back in metres (web-ifc applies the model's length unit).
    api.StreamAllMeshes(modelId, (flatMesh) => {
      const guid: string | undefined = api.GetLine(modelId, flatMesh.expressID)?.GlobalId?.value
      const color = guid ? deviceColors.get(guid) : undefined

      for (let i = 0; i < flatMesh.geometries.size(); i++) {
        const placed = flatMesh.geometries.get(i)
        const geometry = api.GetGeometry(modelId, placed.geometryExpressID)
        const vertices = api.GetVertexArray(geometry.GetVertexData(), geometry.GetVertexDataSize())
        const indices = api.GetIndexArray(geometry.GetIndexData(), geometry.GetIndexDataSize())

        const mesh = new THREE.Mesh(
          toBufferGeometry(vertices, indices),
          color
            ? new THREE.MeshStandardMaterial({ color, emissive: color, emissiveIntensity: 0, roughness: 0.55 })
            : buildingMaterial.clone(),
        )
        mesh.matrixAutoUpdate = false
        mesh.matrix.fromArray(placed.flatTransformation)

        if (color && guid) {
          mesh.userData.deviceId = guid
          handles.devices.set(guid, [...(handles.devices.get(guid) ?? []), mesh])
        } else {
          mesh.renderOrder = 1 // draw the translucent building after the devices
        }
        handles.root.add(mesh)
        geometry.delete()
        count++
      }
    })
  } finally {
    api.CloseModel(modelId)
    buildingMaterial.dispose()
  }
  return count
}

/** web-ifc vertices are interleaved: x, y, z, nx, ny, nz per vertex. */
function toBufferGeometry(vertices: Float32Array, indices: Uint32Array): THREE.BufferGeometry {
  const count = vertices.length / 6
  const positions = new Float32Array(count * 3)
  const normals = new Float32Array(count * 3)
  for (let i = 0; i < count; i++) {
    positions.set(vertices.subarray(i * 6, i * 6 + 3), i * 3)
    normals.set(vertices.subarray(i * 6 + 3, i * 6 + 6), i * 3)
  }
  const geometry = new THREE.BufferGeometry()
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3))
  geometry.setAttribute('normal', new THREE.BufferAttribute(normals, 3))
  geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(indices), 1))
  return geometry
}

/** Resolves a theme color token (which may be oklch) to the sRGB value three.js needs, via a 1×1 canvas. */
function toneColor(tone: StatusTone): THREE.Color {
  const value = getComputedStyle(document.documentElement).getPropertyValue(TONE_VARS[tone]).trim()
  const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true })
  if (!context || !value) return new THREE.Color(0x888888)
  context.fillStyle = value
  context.fillRect(0, 0, 1, 1)
  const [r, g, b] = context.getImageData(0, 0, 1, 1).data
  return new THREE.Color().setRGB(r / 255, g / 255, b / 255, THREE.SRGBColorSpace)
}
