import { useEffect, useRef, useState } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js'
import {
  IFCBUILDINGSTOREY,
  IFCCOLUMN,
  IFCDUCTFITTING,
  IFCDUCTSEGMENT,
  IFCSLAB,
  IFCWALL,
  IFCWINDOW,
  IfcAPI,
} from 'web-ifc'
import wasmUrl from 'web-ifc/web-ifc.wasm?url'
import { Loader2Icon, RotateCcwIcon } from 'lucide-react'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import type { AuditDevice } from '@/lib/api'
import { STATUS_INFO, type StatusTone } from '@/lib/status'
import { cn } from '@/lib/utils'

interface Props {
  file: File
  devices: AuditDevice[]
  /** Devices outside the current filter are drawn faded. */
  visibleIds: Set<string>
  selectedId: string | null
  onSelect: (id: string | null) => void
}

type LoadState = { kind: 'loading' } | { kind: 'ready' } | { kind: 'empty' } | { kind: 'error'; message: string }

const TONE_VARS: Record<StatusTone, string> = {
  success: '--chart-ok',
  warning: '--chart-fixable',
  danger: '--chart-designer',
}

const ALL_LEVELS = 'all'

/**
 * Draws the IFC model in the browser with web-ifc (WebAssembly) and three.js: a ghosted building with real
 * materials for glass, concrete and galvanised ductwork, and the audited devices colored by result. Works with
 * mouse and touch. Nothing is sent to the server.
 */
export default function ModelViewer({ file, devices, visibleIds, selectedId, onSelect }: Props) {
  const container = useRef<HTMLDivElement>(null)
  const scene = useRef<SceneHandles | null>(null)
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [levels, setLevels] = useState<string[]>([])
  const [level, setLevel] = useState(ALL_LEVELS)
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
      .then((storeys) => {
        if (disposed) return
        setLevels(storeys)
        setState(handles.root.children.length === 0 ? { kind: 'empty' } : { kind: 'ready' })
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

  function chooseLevel(next: string) {
    setLevel(next)
    scene.current?.showLevel(next === ALL_LEVELS ? null : next)
    scene.current?.fit()
  }

  const hoveredDevice = hovered ? byId.get(hovered.id) : undefined

  return (
    <div className="relative h-[380px] overflow-hidden bg-gradient-to-b from-sky-50 to-background sm:h-[480px] lg:h-[560px] dark:from-slate-900 dark:to-background">
      <div ref={container} className="absolute inset-0 touch-none" />

      {state.kind !== 'ready' && (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 px-6 text-center text-sm text-muted-foreground">
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
          <div className="absolute top-2 left-2 flex items-center gap-x-3 rounded-lg border bg-card/90 px-2.5 py-1.5 text-xs shadow-sm backdrop-blur sm:top-3 sm:left-3">
            {(['success', 'warning', 'danger'] as const).map((tone) => (
              <span key={tone} className="flex items-center gap-1.5">
                <span className="size-2.5 rounded-sm" style={{ background: `var(${TONE_VARS[tone]})` }} />
                {tone === 'success' ? 'OK' : tone === 'warning' ? 'Fixable' : 'Designer'}
              </span>
            ))}
          </div>

          <Button
            variant="outline"
            size="sm"
            className="absolute top-2 right-2 bg-card/90 sm:top-3 sm:right-3"
            onClick={() => scene.current?.fit()}
            aria-label="Reset view"
          >
            <RotateCcwIcon /> <span className="hidden sm:inline">Reset view</span>
          </Button>

          {levels.length > 1 && (
            <div
              className="absolute bottom-2 left-2 flex rounded-lg border bg-card/90 p-0.5 text-xs shadow-sm backdrop-blur sm:bottom-3 sm:left-3"
              role="group"
              aria-label="Show level"
            >
              {[ALL_LEVELS, ...levels].map((l) => (
                <button
                  key={l}
                  type="button"
                  aria-pressed={level === l}
                  onClick={() => chooseLevel(l)}
                  className={cn(
                    'rounded-md px-2.5 py-1 whitespace-nowrap text-muted-foreground transition-colors hover:text-foreground',
                    level === l && 'bg-primary font-medium text-primary-foreground hover:text-primary-foreground',
                  )}
                >
                  {l === ALL_LEVELS ? 'All levels' : l}
                </button>
              ))}
            </div>
          )}

          <p className="pointer-events-none absolute right-3 bottom-3 hidden text-xs text-muted-foreground md:block">
            Drag to orbit · scroll to zoom · click a device
          </p>
          <p className="pointer-events-none absolute right-2 bottom-2.5 text-[11px] text-muted-foreground md:hidden">
            Drag · pinch · tap
          </p>
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
  showLevel: (level: string | null) => void
  highlight: (visible: Set<string>, selected: string | null) => void
  dispose: () => void
}

function createScene(host: HTMLDivElement): SceneHandles {
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true })
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
  renderer.setSize(host.clientWidth, host.clientHeight)
  renderer.toneMapping = THREE.ACESFilmicToneMapping
  renderer.toneMappingExposure = 1.05
  renderer.shadowMap.enabled = true
  renderer.shadowMap.type = THREE.PCFSoftShadowMap
  host.appendChild(renderer.domElement)

  const scene = new THREE.Scene()
  // Soft studio reflections, so metal ductwork and glass read as materials rather than flat colors.
  const pmrem = new THREE.PMREMGenerator(renderer)
  const environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture
  scene.environment = environment
  pmrem.dispose()

  const camera = new THREE.PerspectiveCamera(40, host.clientWidth / host.clientHeight, 0.1, 2000)
  const controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true
  controls.maxPolarAngle = Math.PI * 0.49 // don't orbit under the ground
  // A slow turntable until the first interaction, unless the visitor prefers reduced motion.
  controls.autoRotate = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
  controls.autoRotateSpeed = 0.6
  controls.addEventListener('start', () => (controls.autoRotate = false))

  scene.add(new THREE.HemisphereLight(0xf4f7fb, 0x9aa6b5, 1.1))
  const sun = new THREE.DirectionalLight(0xfff4e5, 2.2)
  sun.castShadow = true
  sun.shadow.mapSize.set(2048, 2048)
  sun.shadow.bias = -0.0005
  sun.shadow.normalBias = 0.02
  scene.add(sun, sun.target)

  // Ground: a soft shadow catcher and a faint grid, so the building sits on something.
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.ShadowMaterial({ opacity: 0.18 }))
  ground.rotation.x = -Math.PI / 2
  ground.receiveShadow = true
  const grid = new THREE.GridHelper(1, 1, 0x94a3b8, 0x94a3b8)
  ;(grid.material as THREE.Material).transparent = true
  ;(grid.material as THREE.Material).opacity = 0.18
  scene.add(ground, grid)

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
      const box = new THREE.Box3()
      root.traverseVisible((o) => o instanceof THREE.Mesh && box.expandByObject(o))
      if (box.isEmpty()) return
      const center = box.getCenter(new THREE.Vector3())
      const size = box.getSize(new THREE.Vector3()).length()

      // Back off until the model fits the narrower of the two fields of view. A bounding-sphere fit is too
      // conservative for long, flat buildings, so use the tangent fit on the box diagonal, with 10% room so the
      // corners stay inside the frame while the turntable rotates.
      const verticalFov = THREE.MathUtils.degToRad(camera.fov)
      const fov = Math.min(verticalFov, 2 * Math.atan(Math.tan(verticalFov / 2) * camera.aspect))
      const distance = (size / 2 / Math.tan(fov / 2)) * 1.1
      camera.position.copy(center).add(new THREE.Vector3(0.6, 0.55, 0.75).normalize().multiplyScalar(distance))
      camera.near = size / 200
      camera.far = size * 10
      camera.updateProjectionMatrix()
      controls.target.copy(center)
      controls.update()

      // Ground, grid and sun follow the model's size.
      const floor = box.min.y - 0.01
      const span = size * 3
      ground.scale.set(span, span, 1)
      ground.position.set(center.x, floor, center.z)
      grid.scale.set(span, 1, span)
      grid.position.set(center.x, floor + 0.001, center.z)
      sun.position.copy(center).add(new THREE.Vector3(-0.5, 1, 0.35).multiplyScalar(size))
      sun.target.position.copy(center)
      const shadow = sun.shadow.camera
      shadow.left = shadow.bottom = -size * 0.7
      shadow.right = shadow.top = size * 0.7
      shadow.near = 0.1
      shadow.far = size * 3
      shadow.updateProjectionMatrix()
    },
    showLevel(level) {
      root.traverse((o) => {
        if (!(o instanceof THREE.Mesh)) return
        // Elements outside any storey (rare, e.g. site objects) stay visible rather than vanish.
        const onLevel = level === null || o.userData.level === undefined || o.userData.level === level
        // Viewing a single level means looking into it, so its roof is hidden.
        o.visible = onLevel && !(level !== null && o.userData.isRoof)
      })
    },
    highlight(visible, selected) {
      for (const [id, meshes] of handles.devices) {
        const faded = !visible.has(id) || (selected !== null && selected !== id)
        for (const mesh of meshes) {
          const material = mesh.material as THREE.MeshStandardMaterial
          material.opacity = faded ? 0.2 : 1
          material.transparent = faded
          material.emissiveIntensity = id === selected ? 0.55 : 0.08
        }
      }
    },
    dispose() {
      cancelAnimationFrame(frame)
      resize.disconnect()
      renderer.domElement.removeEventListener('pointermove', onPointerMove)
      renderer.domElement.removeEventListener('pointerdown', onPointerDown)
      renderer.domElement.removeEventListener('pointerup', onPointerUp)
      renderer.domElement.removeEventListener('pointerleave', onPointerLeave)
      scene.traverse((object) => {
        if (object instanceof THREE.Mesh || object instanceof THREE.LineSegments) {
          object.geometry.dispose()
          ;(object.material as THREE.Material).dispose()
        }
      })
      environment.dispose()
      controls.dispose()
      renderer.dispose()
      renderer.domElement.remove()
    },
  }

  // Picking: only devices are pickable, so a tap on a wall doesn't hide the device behind it.
  const raycaster = new THREE.Raycaster()
  const pointer = new THREE.Vector2()
  const pick = (event: PointerEvent): string | null => {
    const rect = renderer.domElement.getBoundingClientRect()
    pointer.set(((event.clientX - rect.left) / rect.width) * 2 - 1, -((event.clientY - rect.top) / rect.height) * 2 + 1)
    raycaster.setFromCamera(pointer, camera)
    const pickable = [...handles.devices.values()].flat().filter((m) => m.visible)
    const hit = raycaster.intersectObjects(pickable, false)[0]
    return (hit?.object.userData.deviceId as string | undefined) ?? null
  }
  const onPointerMove = (event: PointerEvent) => {
    // Touch screens have no hover; the tooltip would stick under the finger.
    if (event.pointerType !== 'mouse') return
    const id = pick(event)
    const rect = renderer.domElement.getBoundingClientRect()
    renderer.domElement.style.cursor = id ? 'pointer' : 'grab'
    handles.onHover(id, event.clientX - rect.left, event.clientY - rect.top)
  }
  const onPointerLeave = () => handles.onHover(null, 0, 0)
  // A tap or click only selects when the pointer didn't move, so orbiting and pinching don't change the selection.
  let downAt: { x: number; y: number } | null = null
  const onPointerDown = (event: PointerEvent) => (downAt = { x: event.clientX, y: event.clientY })
  const onPointerUp = (event: PointerEvent) => {
    if (downAt && Math.hypot(event.clientX - downAt.x, event.clientY - downAt.y) < 6) handles.onClick(pick(event))
    downAt = null
  }
  renderer.domElement.addEventListener('pointermove', onPointerMove)
  renderer.domElement.addEventListener('pointerdown', onPointerDown)
  renderer.domElement.addEventListener('pointerup', onPointerUp)
  renderer.domElement.addEventListener('pointerleave', onPointerLeave)

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

// ---- Loading the IFC file ------------------------------------------------------------------------------------------

type Look = 'wall' | 'slab' | 'roof' | 'window' | 'column' | 'duct' | 'other'

/** Physically based materials per kind of element. The building is ghosted so the devices inside stay visible. */
function materialFor(look: Look): THREE.MeshStandardMaterial {
  const ghost = { transparent: true, depthWrite: false, side: THREE.DoubleSide } as const
  switch (look) {
    case 'wall':
      return new THREE.MeshStandardMaterial({ color: 0xe9e4dc, roughness: 0.9, opacity: 0.2, ...ghost })
    case 'slab':
      return new THREE.MeshStandardMaterial({ color: 0xd3d8de, roughness: 0.85, opacity: 0.55, ...ghost })
    case 'roof':
      return new THREE.MeshStandardMaterial({ color: 0xc9ced5, roughness: 0.85, opacity: 0.12, ...ghost })
    case 'window':
      return new THREE.MeshStandardMaterial({ color: 0x8fb9dd, roughness: 0.05, metalness: 0.2, opacity: 0.4, ...ghost })
    case 'column':
      return new THREE.MeshStandardMaterial({ color: 0xbcc3cb, roughness: 0.8, opacity: 0.7, transparent: true })
    case 'duct':
      // Galvanised steel: opaque and metallic, reflecting the environment.
      return new THREE.MeshStandardMaterial({ color: 0xc8ced6, roughness: 0.32, metalness: 0.85 })
    default:
      return new THREE.MeshStandardMaterial({ color: 0xb8c2d0, roughness: 0.8, opacity: 0.2, ...ghost })
  }
}

const EDGED: Look[] = ['wall', 'slab', 'roof', 'column', 'window']

/** Streams every mesh of the IFC file into the scene. Returns the storey names, lowest first. */
async function loadModel(file: File, handles: SceneHandles, deviceColors: Map<string, THREE.Color>): Promise<string[]> {
  const api = new IfcAPI()
  // Single-threaded: the multi-threaded build needs cross-origin isolation headers the site doesn't send.
  await api.Init(() => wasmUrl, true)
  const modelId = api.OpenModel(new Uint8Array(await file.arrayBuffer()))
  if (modelId < 0) throw new Error('web-ifc could not open the file.')

  try {
    // Which storey each element is on, for the level selector.
    const levelOf = new Map<number, string>()
    const storeys: { name: string; elevation: number }[] = []
    const visit = (node: { expressID: number; type: string; children: typeof node[] }, level: string | null) => {
      let current = level
      if (node.type === 'IFCBUILDINGSTOREY' || api.GetLineType(modelId, node.expressID) === IFCBUILDINGSTOREY) {
        const line = api.GetLine(modelId, node.expressID)
        current = line?.Name?.value ?? `Level ${storeys.length + 1}`
        storeys.push({ name: current!, elevation: Number(line?.Elevation?.value ?? storeys.length) })
      }
      if (current) levelOf.set(node.expressID, current)
      node.children.forEach((child) => visit(child, current))
    }
    visit(await api.properties.getSpatialStructure(modelId), null)

    const lookOf = (expressId: number): Look => {
      switch (api.GetLineType(modelId, expressId)) {
        case IFCWALL:
          return 'wall'
        case IFCSLAB:
          return api.GetLine(modelId, expressId)?.PredefinedType?.value === 'ROOF' ? 'roof' : 'slab'
        case IFCWINDOW:
          return 'window'
        case IFCCOLUMN:
          return 'column'
        case IFCDUCTSEGMENT:
        case IFCDUCTFITTING:
          return 'duct'
        default:
          return 'other'
      }
    }

    // Geometry comes back in metres (web-ifc applies the model's length unit).
    api.StreamAllMeshes(modelId, (flatMesh) => {
      const guid: string | undefined = api.GetLine(modelId, flatMesh.expressID)?.GlobalId?.value
      const deviceColor = guid ? deviceColors.get(guid) : undefined
      const look = deviceColor ? null : lookOf(flatMesh.expressID)

      for (let i = 0; i < flatMesh.geometries.size(); i++) {
        const placed = flatMesh.geometries.get(i)
        const ifcGeometry = api.GetGeometry(modelId, placed.geometryExpressID)
        const geometry = toBufferGeometry(
          api.GetVertexArray(ifcGeometry.GetVertexData(), ifcGeometry.GetVertexDataSize()),
          api.GetIndexArray(ifcGeometry.GetIndexData(), ifcGeometry.GetIndexDataSize()),
        )
        ifcGeometry.delete()

        const mesh = new THREE.Mesh(
          geometry,
          deviceColor
            ? new THREE.MeshStandardMaterial({ color: deviceColor, emissive: deviceColor, emissiveIntensity: 0.08, roughness: 0.45, metalness: 0.15 })
            : materialFor(look!),
        )
        mesh.matrixAutoUpdate = false
        mesh.matrix.fromArray(placed.flatTransformation)
        mesh.userData.level = levelOf.get(flatMesh.expressID)

        if (deviceColor && guid) {
          mesh.userData.deviceId = guid
          mesh.castShadow = true
          handles.devices.set(guid, [...(handles.devices.get(guid) ?? []), mesh])
        } else {
          mesh.userData.isRoof = look === 'roof'
          mesh.castShadow = look === 'duct' || look === 'column'
          mesh.receiveShadow = look === 'slab'
          mesh.renderOrder = look === 'duct' ? 0 : 1 // translucent shells after opaque parts
          if (EDGED.includes(look!)) {
            // Thin outlines give the ghosted building its architectural, drawn look.
            const edges = new THREE.LineSegments(
              new THREE.EdgesGeometry(geometry, 30),
              new THREE.LineBasicMaterial({ color: 0x475569, transparent: true, opacity: look === 'window' ? 0.35 : 0.28 }),
            )
            edges.renderOrder = 2
            mesh.add(edges)
          }
        }
        handles.root.add(mesh)
      }
    })

    return storeys.sort((a, b) => a.elevation - b.elevation).map((s) => s.name)
  } finally {
    api.CloseModel(modelId)
  }
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
