// A scripted stand-in for the Rhino side, so the panel runs (and is designed) in a plain browser.
// It speaks exactly the protocol the real host will speak: same events, same order, same shapes.

import type { Bridge } from './bridge.js';
import type {
  AgentInfo,
  Attachment,
  ContextItem,
  ConversationSnapshot,
  HistoryEntry,
  HostEvent,
  PanelCommand,
  ToolCall,
  ToolPatch,
  TokenUsage,
} from './events.js';
import { viewportCapture } from './viewport.js';

const ABORTED = Symbol('aborted');

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));
const lines = (...parts: string[]) => parts.join('\n');

let counter = 0;
const nextId = (prefix: string) => `${prefix}-${++counter}`;

/** One in-flight scripted turn: cancellable, and able to block on an answer. */
class Script {
  private cancelled = false;
  private waiting: ((answers: string[]) => void) | null = null;

  cancel(): void {
    this.cancelled = true;
    this.waiting?.([]);
    this.waiting = null;
  }

  async pause(ms: number): Promise<void> {
    await sleep(ms);
    if (this.cancelled) throw ABORTED;
  }

  answer(values: string[]): void {
    this.waiting?.(values);
    this.waiting = null;
  }

  async awaitAnswer(): Promise<string[]> {
    const values = await new Promise<string[]>((resolve) => {
      this.waiting = resolve;
    });
    if (this.cancelled) throw ABORTED;
    return values;
  }
}

// ---------------------------------------------------------------- fixtures

const AGENTS: readonly AgentInfo[] = [
  { name: 'claude', label: 'Claude Code', model: 'claude-opus-5', modelLabel: 'Opus 5', availability: 'ready', builtin: true },
  { name: 'codex', label: 'Codex', model: 'gpt-5-codex', modelLabel: 'GPT-5 Codex', availability: 'ready', builtin: true },
  {
    name: 'gemini',
    label: 'Gemini CLI',
    model: 'gemini-3-pro',
    modelLabel: 'Gemini 3 Pro',
    availability: 'signin',
    detail: 'run `gemini auth` to sign in',
    builtin: true,
  },
  {
    name: 'local-llama',
    label: 'Local (llama.cpp)',
    model: 'qwen3-coder-30b',
    modelLabel: 'Qwen3 Coder 30B',
    availability: 'missing',
    detail: 'binary not found on PATH',
    builtin: false,
  },
];

const CONTEXT: readonly ContextItem[] = [
  { id: 'ctx-selection', kind: 'selection', label: 'Selection', detail: '3 breps, 1 curve', count: 4 },
  { id: 'ctx-view', kind: 'view', label: 'Perspective', detail: 'active viewport' },
  { id: 'ctx-doc', kind: 'document', label: 'tower-study.3dm', detail: '1,284 objects · 12 layers' },
  { id: 'ctx-layer-facade', kind: 'layer', label: 'Facade::Panels', detail: '312 objects', count: 312 },
  { id: 'ctx-layer-core', kind: 'layer', label: 'Core', detail: '18 objects', count: 18 },
  { id: 'ctx-gh', kind: 'grasshopper', label: 'facade.gh', detail: 'open on canvas' },
];

const HISTORY: readonly HistoryEntry[] = [
  {
    sessionId: 'sess-a',
    title: 'Rationalise the facade into flat panels',
    agent: 'Claude Code',
    docTitle: 'tower-study.3dm',
    startedAt: new Date(Date.now() - 3 * 3600_000).toISOString(),
    turns: 7,
    usage: { inputTokens: 41_200, outputTokens: 8_940, costUsd: 0.62 },
    resumable: true,
  },
  {
    sessionId: 'sess-b',
    title: 'Write a layer-renaming script',
    agent: 'Codex',
    docTitle: 'tower-study.3dm',
    startedAt: new Date(Date.now() - 26 * 3600_000).toISOString(),
    turns: 3,
    usage: { inputTokens: 9_100, outputTokens: 2_300, costUsd: 0.11 },
    resumable: true,
  },
  {
    sessionId: 'sess-c',
    title: 'Why does BooleanUnion fail on these solids?',
    agent: 'Claude Code',
    docTitle: 'bracket.3dm',
    startedAt: new Date(Date.now() - 4 * 86_400_000).toISOString(),
    turns: 11,
    usage: { inputTokens: 88_400, outputTokens: 14_200, costUsd: 1.34 },
    resumable: false,
  },
];

const usage = (input: number, output: number, cost: number): TokenUsage => ({
  inputTokens: input,
  outputTokens: output,
  costUsd: cost,
});

function seeded(): ConversationSnapshot {
  const start = Date.now() - 9 * 60_000;
  return {
    sessionId: 'sess-live',
    agent: 'claude',
    docTitle: 'tower-study.3dm',
    startedAt: new Date(start).toISOString(),
    readOnly: false,
    turns: [
      {
        id: 'turn-seed-1',
        prompt: 'What is on the Facade::Panels layer, and is any of it going to give the fabricator trouble?',
        attachments: [],
        context: [CONTEXT[3] as ContextItem],
        startedAt: new Date(start).toISOString(),
        status: 'ok',
        usage: usage(12_480, 1_120, 0.09),
        undoable: false,
        plan: [],
        blocks: [
          {
            kind: 'tool',
            id: 'call-seed-1',
            call: {
              id: 'call-seed-1',
              name: 'list_objects',
              title: 'listed 312 objects on Facade::Panels',
              args: { layer: 'Facade::Panels', include: ['type', 'area', 'planarity'] },
              status: 'ok',
              durationMs: 410,
              startedAt: new Date(start + 900).toISOString(),
              preview: {
                kind: 'table',
                columns: ['Type', 'Count', 'Planar', 'Notes'],
                rows: [
                  ['Brep', '288', '271', '17 doubly curved'],
                  ['Extrusion', '18', '18', ''],
                  ['Mesh', '6', '6', 'imported, welded'],
                ],
              },
            },
          },
          {
            kind: 'text',
            id: 'block-seed-1',
            at: new Date(start + 1400).toISOString(),
            text: lines(
              'Three hundred and twelve objects, and **17 of them are doubly curved** which is the only real problem in there.',
              '',
              '- 271 breps are planar and will unroll cleanly',
              '- 18 extrusions are fine, they are just boxes',
              '- 6 imported meshes are welded but carry no thickness',
              '- 17 breps have Gaussian curvature in both directions',
              '',
              'The 17 are all on the south-west corner where the surface twists. A fabricator will either cold-bend them or you rationalise them into planar quads. I can tag them onto their own layer so you can look at them in isolation.',
            ),
          },
        ],
      },
      {
        id: 'turn-seed-2',
        prompt: 'Tag them please, and tell me how far off planar they are.',
        attachments: [],
        context: [],
        startedAt: new Date(start + 120_000).toISOString(),
        status: 'ok',
        usage: usage(14_900, 2_040, 0.14),
        undoable: true,
        plan: [
          { id: 'p1', text: 'Measure planarity deviation per panel', state: 'done' },
          { id: 'p2', text: 'Create Facade::Panels::NonPlanar', state: 'done' },
          { id: 'p3', text: 'Move the offending panels', state: 'done' },
        ],
        blocks: [
          {
            kind: 'tool',
            id: 'call-seed-2',
            call: {
              id: 'call-seed-2',
              name: 'run_python',
              title: 'ran python',
              args: { code: '# measure planarity deviation and re-layer' },
              status: 'ok',
              durationMs: 1_260,
              mutated: true,
              startedAt: new Date(start + 121_000).toISOString(),
              preview: {
                kind: 'code',
                language: 'python',
                text: lines(
                  'import Rhino',
                  'import scriptcontext as sc',
                  '',
                  'TOL = sc.doc.ModelAbsoluteTolerance',
                  'target = sc.doc.Layers.FindByFullPath("Facade::Panels::NonPlanar", True)',
                  '',
                  'def deviation(brep):',
                  '    """Max distance from the brep vertices to their best-fit plane."""',
                  '    pts = [v.Location for v in brep.Vertices]',
                  '    ok, plane = Rhino.Geometry.Plane.FitPlaneToPoints(pts)',
                  '    if ok != Rhino.Geometry.PlaneFitResult.Success:',
                  '        return None',
                  '    return max(abs(plane.DistanceTo(p)) for p in pts)',
                  '',
                  'moved = []',
                  'for obj in objects_on("Facade::Panels"):',
                  '    d = deviation(obj.Geometry)',
                  '    if d is not None and d > TOL * 10:',
                  '        obj.Attributes.LayerIndex = target',
                  '        obj.CommitChanges()',
                  '        moved.append((obj.Id, d))',
                  '',
                  'print(f"{len(moved)} panels re-layered")',
                ),
              },
              result: { moved: 17, maxDeviationMm: 41.6, layer: 'Facade::Panels::NonPlanar' },
            },
          },
          {
            kind: 'text',
            id: 'block-seed-2',
            at: new Date(start + 123_000).toISOString(),
            text: lines(
              'Done, they are on `Facade::Panels::NonPlanar` now.',
              '',
              'Deviation from best-fit plane, worst first:',
              '',
              '| Panel | Deviation | Verdict |',
              '| --- | --- | --- |',
              '| L14 corner | 41.6 mm | split into triangles |',
              '| L15 corner | 33.2 mm | split into triangles |',
              '| L12 corner | 31.8 mm | split into triangles |',
              '| L11 corner | 30.4 mm | borderline |',
              '',
              '- 4 panels over 30 mm (worst is **41.6 mm**)',
              '- 7 panels between 10 and 30 mm',
              '- 6 panels under 10 mm, which most glaziers will cold-bend without complaint',
              '',
              '> The 41.6 mm one is the corner panel at level 14. That one wants splitting into two triangles rather than bending.',
              '',
              'Say the word and I will split the four worst into triangles.',
            ),
          },
        ],
      },
    ],
  };
}

function reviewSnapshot(entry: HistoryEntry): ConversationSnapshot {
  return {
    sessionId: entry.sessionId,
    agent: 'claude',
    docTitle: entry.docTitle,
    startedAt: entry.startedAt,
    readOnly: true,
    turns: [
      {
        id: `${entry.sessionId}-t1`,
        prompt: entry.title,
        attachments: [],
        context: [],
        startedAt: entry.startedAt,
        status: 'ok',
        usage: entry.usage,
        undoable: false,
        plan: [],
        blocks: [
          {
            kind: 'text',
            id: `${entry.sessionId}-b1`,
            at: entry.startedAt,
            text: lines(
              `This is a saved transcript from ${new Date(entry.startedAt).toLocaleString()}.`,
              '',
              'Reading it does not touch the live conversation. **Resume** hands it back to the agent with its session id, so the next prompt continues where this left off.',
            ),
          },
        ],
      },
    ],
  };
}

// ---------------------------------------------------------------- scenarios

interface Scenario {
  match: RegExp;
  run(host: MockHost, turnId: string, script: Script, prompt: string): Promise<void>;
}

// Order is significant: the first pattern to match wins, and the starters deliberately overlap
// ("organise ... in the view into layers" contains "view").
const SCENARIOS: readonly Scenario[] = [
  {
    match: /grasshopper|tower|twist|loft|canvas|\bgh\b/i,
    async run(host, turnId, script) {
      host.status('Opening the canvas…');
      host.emit({
        type: 'turn.plan',
        turnId,
        steps: [
          { id: 'p1', text: 'Open the Grasshopper canvas', state: 'active' },
          { id: 'p2', text: 'Stack and rotate the floor plates', state: 'pending' },
          { id: 'p3', text: 'Loft and solve', state: 'pending' },
        ],
      });
      await script.pause(300);

      await host.text(turnId, script, lines('Building it on the canvas so you can keep twisting it afterwards.', ''));

      await host.tool(turnId, script, 'g2_start', 'opened Grasshopper', { file: 'tower.gh' }, 560, {
        result: { canvas: 'tower.gh', components: 0 },
      });

      host.emit({
        type: 'turn.plan',
        turnId,
        steps: [
          { id: 'p1', text: 'Open the Grasshopper canvas', state: 'done' },
          { id: 'p2', text: 'Stack and rotate the floor plates', state: 'active' },
          { id: 'p3', text: 'Loft and solve', state: 'pending' },
        ],
      });
      host.status('Stacking the floors…');

      await host.tool(turnId, script, 'g2_place_component', 'placed Rectangle', { selector: 'Rectangle', at: [140, 200] }, 320, {
        mutated: true,
        result: { guid: '4a2c…', inputs: 3, outputs: 1 },
      });
      await host.tool(
        turnId,
        script,
        'g2_place_slider',
        'placed 3 sliders (floors, height, twist)',
        {
          sliders: [
            { name: 'floors', min: 4, max: 60, value: 24 },
            { name: 'height', min: 2, max: 6, value: 3.4 },
            { name: 'twist', min: 0, max: 360, value: 90 },
          ],
        },
        280,
        { mutated: true },
      );
      await host.tool(turnId, script, 'g2_connect_many', 'wired 7 connections', { pairs: 7 }, 300, { mutated: true });

      host.emit({
        type: 'turn.plan',
        turnId,
        steps: [
          { id: 'p1', text: 'Open the Grasshopper canvas', state: 'done' },
          { id: 'p2', text: 'Stack and rotate the floor plates', state: 'done' },
          { id: 'p3', text: 'Loft and solve', state: 'active' },
        ],
      });
      host.status('Solving…');

      await host.tool(turnId, script, 'g2_solve_canvas', 'solved the graph', {}, 640, {
        preview: {
          kind: 'graph',
          components: ['Rectangle', 'Series', 'Rotate', 'Move', 'Loft'],
          wires: 7,
          errors: 0,
          warnings: 0,
        },
        result: { solved: true, Errors: 0, Warnings: 0, components: 5, runtimeMs: 96 },
      });

      host.status(null);
      await host.text(
        turnId,
        script,
        lines(
          '',
          'Twenty-four floors, 3.4 m apart, rotating to **90 degrees** over the full height.',
          '',
          '- `floors` and `height` set the stack, `twist` sets the total rotation',
          '- `Series` drives both the vertical `Move` and the `Rotate` angle, so they stay in step',
          '- `Loft` through the rotated rectangles gives the skin',
          '',
          'The same thing as a script, if you would rather have it in the document:',
          '',
          '```python',
          'import math',
          'import Rhino.Geometry as rg',
          '',
          'def twisty_tower(floors=24, height=3.4, twist=90.0, width=18.0):',
          '    """Lofted tower whose floor plates rotate linearly with height."""',
          '    plates = []',
          '    for i in range(floors + 1):',
          '        angle = math.radians(twist * i / floors)',
          '        plane = rg.Plane(rg.Point3d(0, 0, i * height), rg.Vector3d.ZAxis)',
          '        plane.Rotate(angle, rg.Vector3d.ZAxis)',
          '        plates.append(rg.Rectangle3d(plane, width, width).ToNurbsCurve())',
          '    return rg.Brep.CreateFromLoft(',
          '        plates, rg.Point3d.Unset, rg.Point3d.Unset, rg.LoftType.Normal, False)',
          '```',
          '',
          'Drag the twist slider and it re-solves live.',
        ),
      );

      host.emit({ type: 'turn.usage', turnId, usage: usage(14_120, 2_180, 0.13) });
      host.emit({ type: 'turn.end', turnId, status: 'ok' });
    },
  },
  {
    match: /organis|organiz|layer|sort|tidy|group/i,
    async run(host, turnId, script) {
      host.status('Reading the view…');
      await host.text(turnId, script, lines('Let me see what is actually in the view before I move anything.', ''));

      await host.tool(turnId, script, 'list_objects', 'listed 486 objects in the view', { scope: 'view' }, 380, {
        preview: {
          kind: 'table',
          columns: ['Type', 'Count', 'Current layer'],
          rows: [
            ['Brep', '312', 'Default'],
            ['Extrusion', '96', 'Default'],
            ['Curve', '54', 'Default'],
            ['Mesh', '18', 'imported'],
            ['Text', '6', 'Default'],
          ],
        },
      });

      host.status('Sorting into layers…');
      await host.tool(turnId, script, 'run_python', 'sorted 486 objects into 5 layers', { strategy: 'by-type' }, 1_040, {
        mutated: true,
        preview: {
          kind: 'code',
          language: 'python',
          text: lines(
            'import scriptcontext as sc',
            'import Rhino',
            '',
            'BUCKETS = {',
            '    Rhino.DocObjects.ObjectType.Brep: "Solids",',
            '    Rhino.DocObjects.ObjectType.Extrusion: "Solids",',
            '    Rhino.DocObjects.ObjectType.Curve: "Curves",',
            '    Rhino.DocObjects.ObjectType.Mesh: "Meshes",',
            '    Rhino.DocObjects.ObjectType.Annotation: "Annotation",',
            '}',
            '',
            'def layer_for(name):',
            '    """Find or create a top-level layer and return its index."""',
            '    found = sc.doc.Layers.FindName(name, -1)',
            '    if found:',
            '        return found.Index',
            '    return sc.doc.Layers.Add(name, System.Drawing.Color.Black)',
            '',
            'moved = 0',
            'for obj in objects_in_view():',
            '    bucket = BUCKETS.get(obj.ObjectType)',
            '    if bucket is None:',
            '        continue',
            '    obj.Attributes.LayerIndex = layer_for(bucket)',
            '    obj.CommitChanges()',
            '    moved += 1',
            '',
            'print(f"moved {moved} objects")',
          ),
        },
        result: { moved: 486, layers: ['Solids', 'Curves', 'Meshes', 'Annotation'], skipped: 0 },
      });

      host.status(null);
      await host.text(
        turnId,
        script,
        lines(
          '',
          'Sorted by object type, since that is what the view actually splits along:',
          '',
          '| Layer | Objects |',
          '| --- | --- |',
          '| Solids | 408 |',
          '| Curves | 54 |',
          '| Meshes | 18 |',
          '| Annotation | 6 |',
          '',
          'Two things worth saying:',
          '',
          '1. The 18 meshes were already on `imported`, and I left that layer in place rather than deleting it.',
          '2. The whole turn is one undo record, so a single Ctrl+Z puts everything back where it was.',
          '',
          'If you would rather sort by size, elevation or name instead, say so and I will redo it.',
        ),
      );

      host.emit({ type: 'turn.usage', turnId, usage: usage(11_300, 1_640, 0.1) });
      host.emit({ type: 'turn.end', turnId, status: 'ok' });
    },
  },
  {
    match: /sphere|command|generate|script|python|c#|random/i,
    async run(host, turnId, script) {
      host.status('Writing the command…');
      await host.text(
        turnId,
        script,
        lines('I will register it as a real Rhino command so you can type it like any other.', ''),
      );

      await host.tool(turnId, script, 'run_python', 'ran python', { register: 'RandomSpheres' }, 1_180, {
        mutated: true,
        preview: {
          kind: 'code',
          language: 'python',
          text: lines(
            'import random',
            'import Rhino.Geometry as rg',
            'import scriptcontext as sc',
            '',
            'BOUNDS = rg.BoundingBox(rg.Point3d(-50, -50, 0), rg.Point3d(50, 50, 40))',
            '',
            'def random_spheres(count=100, min_radius=0.5, max_radius=4.0):',
            '    """Scatter `count` spheres through BOUNDS and add them to the document."""',
            '    added = []',
            '    for _ in range(count):',
            '        centre = rg.Point3d(',
            '            random.uniform(BOUNDS.Min.X, BOUNDS.Max.X),',
            '            random.uniform(BOUNDS.Min.Y, BOUNDS.Max.Y),',
            '            random.uniform(BOUNDS.Min.Z, BOUNDS.Max.Z))',
            '        sphere = rg.Sphere(centre, random.uniform(min_radius, max_radius))',
            '        added.append(sc.doc.Objects.AddSphere(sphere))',
            '    sc.doc.Views.Redraw()',
            '    return added',
            '',
            'ids = random_spheres()',
            'print(f"added {len(ids)} spheres")',
          ),
        },
        result: { command: 'RandomSpheres', added: 100, stdout: 'added 100 spheres' },
      });

      await host.tool(turnId, script, 'get_selection', 'selected the 100 new spheres', {}, 220, {
        preview: {
          kind: 'objects',
          items: [
            { id: 'obj-1', label: 'Sphere r=3.42', layer: 'Default' },
            { id: 'obj-2', label: 'Sphere r=1.08', layer: 'Default' },
            { id: 'obj-3', label: 'Sphere r=2.71', layer: 'Default' },
            { id: 'obj-4', label: 'and 97 more', layer: 'Default' },
          ],
        },
        result: { count: 100 },
      });

      host.status(null);
      await host.text(
        turnId,
        script,
        lines(
          '',
          'Done. Type `RandomSpheres` to run it again.',
          '',
          '- radii between 0.5 and 4.0, centres scattered through a 100 × 100 × 40 box',
          '- the new spheres are selected, so `Delete` undoes the scatter without touching anything else',
          '- one undo record for the whole run',
          '',
          'Want the count, the bounds or the radius range exposed as command options?',
        ),
      );

      host.emit({ type: 'turn.usage', turnId, usage: usage(9_800, 1_920, 0.09) });
      host.emit({ type: 'turn.end', turnId, status: 'ok' });
    },
  },
  {
    match: /select|audit|brep|bad|problem|tiny|edge|check/i,
    async run(host, turnId, script) {
      host.status('Reading the selection…');
      await host.tool(turnId, script, 'get_selection', 'read the selection (4 objects)', {}, 260, {
        preview: {
          kind: 'objects',
          items: [
            { id: 'obj-1', label: 'Brep · corner panel L14', layer: 'Facade::Panels' },
            { id: 'obj-2', label: 'Brep · corner panel L15', layer: 'Facade::Panels' },
            { id: 'obj-3', label: 'Brep · mullion cap', layer: 'Facade::Mullions' },
            { id: 'obj-4', label: 'Curve · setting-out line', layer: 'Default' },
          ],
        },
        result: { count: 4, types: { Brep: 3, Curve: 1 } },
      });

      await host.text(turnId, script, lines('Three breps and a curve. Let me run the bad-object check over them.', ''));

      host.status('Running SelBadObjects…');
      await host.failingTool(
        turnId,
        script,
        'run_command',
        'SelBadObjects failed',
        { command: 'SelBadObjects', echo: true },
        520,
        'Command "SelBadObjects" needs the whole document, not a selection subset. Rhino returned: nothing selected.',
      );

      await host.text(
        turnId,
        script,
        lines(
          '',
          'That command only works document-wide, so I checked the three breps directly instead:',
          '',
          '- **corner panel L14**: one naked edge, 0.4 mm gap at the mitre',
          '- **corner panel L15**: clean',
          '- **mullion cap**: two coincident faces, which is what would break a boolean later',
          '',
          'How do you want the mitre gap handled?',
        ),
      );

      host.status(null);
      // Two at once: one ask_user call poses a batch, and the panel has to stack them under one
      // submit rather than showing only the last.
      const questionIds = [nextId('q'), nextId('q')];
      host.emit({
        type: 'question',
        question: {
          id: questionIds[0] as string,
          question: 'The L14 mitre has a 0.4 mm gap. How should I close it?',
          options: [
            'Raise the join tolerance and re-join',
            'Extend the face and re-trim',
            'Leave it and flag it to the fabricator',
          ],
          mode: 'single',
          allowOther: true,
        },
      });
      host.emit({
        type: 'question',
        question: {
          id: questionIds[1] as string,
          question: 'Which panels should I check the same way?',
          options: ['The rest of level 14', 'Every mitred panel', 'Only the ones I flagged'],
          mode: 'multi',
          allowOther: true,
        },
      });

      const answers = await script.awaitAnswer();
      for (const id of questionIds) host.emit({ type: 'question.clear', id });
      if (answers.length === 0) {
        host.emit({ type: 'turn.usage', turnId, usage: usage(11_200, 1_460, 0.1) });
        host.emit({ type: 'turn.end', turnId, status: 'ok' });
        return;
      }

      host.status('Applying…');
      await host.tool(turnId, script, 'run_python', `applied: ${answers.join(', ')}`, { strategy: answers }, 860, {
        mutated: true,
        result: { joined: true, nakedEdges: 0, tolerance: 0.01 },
      });

      host.status(null);
      await host.text(
        turnId,
        script,
        lines(
          '',
          `Closed it via **${answers[0]}**. The panel reports zero naked edges now.`,
          '',
          'The coincident faces on the mullion cap are still there. That one is a modelling decision rather than a tolerance one, so I left it alone.',
        ),
      );

      host.emit({ type: 'turn.usage', turnId, usage: usage(15_800, 2_240, 0.16) });
      host.emit({ type: 'turn.end', turnId, status: 'ok' });
    },
  },
  {
    match: /view|camera|capture|render|screenshot|quarter/i,
    async run(host, turnId, script) {
      host.status('Framing the view…');
      await host.tool(
        turnId,
        script,
        'set_camera',
        'set camera to a three-quarter view',
        { target: [0, 0, 24], azimuth: 35, altitude: 22, lens: 40 },
        340,
        { result: { view: 'Perspective', lens: 40 } },
      );

      await host.tool(turnId, script, 'get_viewport_image', 'captured Perspective', { width: 1120, height: 680 }, 700, {
        preview: { kind: 'image', dataUrl: viewportCapture(), caption: 'Perspective · 1120 × 680 · shaded' },
        result: { width: 1120, height: 680, display: 'Shaded' },
      });

      host.status(null);
      await host.text(
        turnId,
        script,
        lines(
          'Framed at a 40 mm lens from the south-west, which keeps the tower vertical without the wide-angle stretch you get at 20 mm.',
          '',
          'Two things I would change before this goes in a document:',
          '',
          '1. The horizon sits exactly at the level-14 slab, so the corner panels read as a single line. Drop the camera ~2 m.',
          '2. Shaded mode is flattening the mullion depth. `Arctic` or a rendered display mode would show it.',
          '',
          'Want me to capture both variants so you can compare?',
        ),
      );

      host.emit({ type: 'turn.usage', turnId, usage: usage(7_400, 980, 0.06) });
      host.emit({ type: 'turn.end', turnId, status: 'ok' });
    },
  },
  {
    match: /.*/,
    async run(host, turnId, script, prompt) {
      host.status('Thinking…');
      await script.pause(420);
      host.status(null);
      await host.text(
        turnId,
        script,
        lines(
          `Taking *"${prompt.slice(0, 72)}"* at face value, here is where I would start.`,
          '',
          'This panel is a prototype, so the reply is scripted, but everything around it is real: the streaming, the tool cards, the markdown, the keyboard handling and the layout are all doing what they would do against a live agent.',
          '',
          'Try one of these to see the interesting paths:',
          '',
          '- something with **tower** or **grasshopper**: plan steps, five tool calls, a solved-graph card',
          '- something with **spheres** or **command**: a script card with highlighted source',
          '- something with **layers** or **organise**: a table result and a summary',
          '- something with **selected** or **problems**: a failing tool card and an inline question',
          '- something with **capture** or **view**: an image result rendered in place',
          '',
          'Or press `/` for commands and `@` to attach document context.',
        ),
      );
      host.emit({ type: 'turn.usage', turnId, usage: usage(4_200, 640, 0.03) });
      host.emit({ type: 'turn.end', turnId, status: 'ok' });
    },
  },
];

// ---------------------------------------------------------------- the host

export class MockHost implements Bridge {
  readonly kind = 'mock' as const;

  private readonly handlers = new Set<(event: HostEvent) => void>();
  private script: Script | null = null;
  private activeTurn: string | null = null;
  private booted = false;

  subscribe(handler: (event: HostEvent) => void): () => void {
    this.handlers.add(handler);
    if (!this.booted) {
      this.booted = true;
      queueMicrotask(() => this.boot());
    }
    return () => {
      this.handlers.delete(handler);
    };
  }

  send(command: PanelCommand): void {
    switch (command.type) {
      case 'prompt':
        void this.runTurn(command.request.text, command.request.attachments, command.request.context);
        return;

      case 'cancel':
        if (this.script && this.activeTurn) {
          this.script.cancel();
          this.emit({ type: 'status', text: null });
          this.emit({ type: 'turn.end', turnId: this.activeTurn, status: 'cancelled' });
          this.script = null;
          this.activeTurn = null;
        }
        return;

      case 'conversation.new':
        this.script?.cancel();
        this.script = null;
        this.activeTurn = null;
        this.emit({ type: 'status', text: null });
        this.emit({
          type: 'conversation',
          snapshot: {
            sessionId: nextId('sess'),
            agent: AGENTS[0]?.name ?? 'claude',
            docTitle: 'tower-study.3dm',
            startedAt: new Date().toISOString(),
            readOnly: false,
            turns: [],
          },
        });
        return;

      case 'conversation.load': {
        const entry = HISTORY.find((candidate) => candidate.sessionId === command.sessionId);
        if (entry) this.emit({ type: 'conversation', snapshot: reviewSnapshot(entry) });
        return;
      }

      case 'conversation.resume':
        this.emit({ type: 'conversation', snapshot: seeded() });
        this.notice('info', 'Resumed the saved session; the next prompt continues it.');
        return;

      case 'conversation.exitReview':
        this.emit({ type: 'conversation', snapshot: seeded() });
        return;

      case 'agent.select': {
        this.emit({ type: 'agents', agents: [...AGENTS], active: command.name });
        const picked = AGENTS.find((agent) => agent.name === command.name);
        if (picked) this.notice('info', `Switched to ${picked.label} (${picked.modelLabel}).`);
        return;
      }

      case 'question.answer':
        this.script?.answer(command.items.flatMap((item) => item.answers));
        return;

      case 'question.dismiss':
        for (const id of command.ids) this.emit({ type: 'question.clear', id });
        this.script?.answer([]);
        return;

      case 'turn.undo':
        this.notice('info', 'Reverted every document change that turn made (one Rhino undo record).');
        return;

      case 'turn.retry':
        this.notice('info', 'A real host would re-send that prompt to the agent.');
        return;

      case 'context.reveal':
        this.notice('info', `Selected and zoomed to ${command.id} in the viewport.`);
        return;

      case 'attachments.pick':
        this.notice('info', 'The host would open a file dialog here. Drag a file onto the composer instead.');
        return;

      case 'settings.open':
        this.notice('info', 'AI settings would open as a Rhino options page.');
        return;

      case 'url.open':
        window.open(command.url, '_blank', 'noopener');
        return;

      case 'clipboard.write':
        void navigator.clipboard?.writeText(command.text);
        return;

      default:
        return;
    }
  }

  emit(event: HostEvent): void {
    for (const handler of this.handlers) handler(event);
  }

  status(text: string | null): void {
    this.emit({ type: 'status', text });
  }

  notice(level: 'info' | 'warn' | 'error', text: string): void {
    this.emit({ type: 'notice', level, text });
  }

  /** Word-at-a-time streaming, roughly the cadence a CLI agent produces. */
  async text(turnId: string, script: Script, body: string): Promise<void> {
    const blockId = nextId('block');
    const chunks = body.match(/\S+\s*|\s+/g) ?? [];
    for (let i = 0; i < chunks.length; i += 2) {
      this.emit({
        type: 'turn.text',
        turnId,
        blockId,
        delta: chunks.slice(i, i + 2).join(''),
      });
      await script.pause(20);
    }
  }

  async tool(
    turnId: string,
    script: Script,
    name: string,
    title: string,
    args: unknown,
    workMs: number,
    patch: ToolPatch,
  ): Promise<void> {
    const call: ToolCall = {
      id: nextId('call'),
      name,
      title,
      args,
      status: 'running',
      startedAt: new Date().toISOString(),
    };
    this.emit({ type: 'turn.tool', turnId, call });
    await script.pause(workMs);
    this.emit({
      type: 'turn.tool.patch',
      turnId,
      callId: call.id,
      patch: { status: 'ok', title, durationMs: workMs, ...patch },
    });
  }

  async failingTool(
    turnId: string,
    script: Script,
    name: string,
    title: string,
    args: unknown,
    workMs: number,
    error: string,
  ): Promise<void> {
    const id = nextId('call');
    this.emit({
      type: 'turn.tool',
      turnId,
      call: { id, name, title, args, status: 'running', startedAt: new Date().toISOString() },
    });
    await script.pause(workMs);
    this.emit({
      type: 'turn.tool.patch',
      turnId,
      callId: id,
      patch: { status: 'failed', title, durationMs: workMs, error },
    });
  }

  private boot(): void {
    this.emit({
      type: 'hello',
      host: {
        product: 'Rhinoceros',
        version: '9.0.25246 (prototype host)',
        platform: navigator.platform.toLowerCase().includes('mac') ? 'macos' : 'windows',
        docTitle: 'tower-study.3dm',
        capabilities: { attachments: true, viewportCapture: true, undoTurn: true, grasshopper: true },
      },
    });
    this.emit({
      type: 'theme',
      scheme: window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark',
    });
    this.emit({ type: 'agents', agents: [...AGENTS], active: 'claude' });
    this.emit({ type: 'context', items: [...CONTEXT] });
    this.emit({ type: 'history', entries: [...HISTORY] });
    this.emit({ type: 'conversation', snapshot: seeded() });
  }

  private async runTurn(
    prompt: string,
    attachments: readonly Attachment[],
    context: readonly ContextItem[],
  ): Promise<void> {
    this.script?.cancel();
    const script = new Script();
    this.script = script;

    const turnId = nextId('turn');
    this.activeTurn = turnId;

    this.emit({
      type: 'turn.begin',
      turn: {
        id: turnId,
        prompt,
        attachments: [...attachments],
        context: [...context],
        startedAt: new Date().toISOString(),
        status: 'running',
        usage: null,
        blocks: [],
        plan: [],
        undoable: true,
      },
    });

    const scenario = SCENARIOS.find((candidate) => candidate.match.test(prompt)) ?? (SCENARIOS.at(-1) as Scenario);
    try {
      await scenario.run(this, turnId, script, prompt);
    } catch (error) {
      if (error !== ABORTED) {
        this.emit({ type: 'turn.end', turnId, status: 'error', error: String(error) });
        this.status(null);
      }
      return;
    } finally {
      if (this.script === script) {
        this.script = null;
        this.activeTurn = null;
      }
    }
  }
}
