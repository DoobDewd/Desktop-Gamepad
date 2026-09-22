// Fixed data behind the screens, taken from the Desktop Gamepad design (design/unpacked/design_script_1.jsx).

export const ACCENT = '#60cdff';

export const GLYPHS = {
  xbox: { a: 'A', b: 'B', x: 'X', y: 'Y', lb: 'LB', rb: 'RB', lt: 'LT', rt: 'RT', view: 'View', menu: 'Menu' },
  playstation: { a: 'Cross', b: 'Circle', x: 'Square', y: 'Triangle', lb: 'L1', rb: 'R1', lt: 'L2', rt: 'R2', view: 'Create', menu: 'Options' },
  generic: { a: 'B1', b: 'B2', x: 'B3', y: 'B4', lb: 'L1', rb: 'R1', lt: 'L2', rt: 'R2', view: 'Select', menu: 'Start' }
};

// Pad art folder per controller kind (assets/pad/<dir>/<input id>.png). Generic pads show text chips.
export const ART_DIR = { xbox: 'xbox', playstation: 'ps', generic: '' };

export const INPUTS = [
  { id: 'lt', key: 'lt', label: 'Left trigger' },
  { id: 'lb', key: 'lb', label: 'Left bumper' },
  { id: 'rt', key: 'rt', label: 'Right trigger' },
  { id: 'rb', key: 'rb', label: 'Right bumper' },
  { id: 'lstick', key: null, label: 'Left stick' },
  { id: 'lclick', key: null, label: 'Left stick click' },
  { id: 'rstick', key: null, label: 'Right stick' },
  { id: 'rclick', key: null, label: 'Right stick click' },
  { id: 'dpadUp', key: null, label: 'D-pad up' },
  { id: 'dpadDown', key: null, label: 'D-pad down' },
  { id: 'dpadLeft', key: null, label: 'D-pad left' },
  { id: 'dpadRight', key: null, label: 'D-pad right' },
  { id: 'view', key: 'view', label: 'View button' },
  { id: 'menu', key: 'menu', label: 'Menu button' },
  { id: 'y', key: 'y', label: 'Y button' },
  { id: 'x', key: 'x', label: 'X button' },
  { id: 'b', key: 'b', label: 'B button' },
  { id: 'a', key: 'a', label: 'A button' }
];

export const INPUT_GROUPS = [
  { title: 'Sticks', ids: ['lstick', 'lclick', 'rstick', 'rclick'] },
  { title: 'Face buttons', ids: ['a', 'b', 'x', 'y'] },
  { title: 'Bumpers and triggers', ids: ['lb', 'rb', 'lt', 'rt'] },
  { title: 'D-pad', ids: ['dpadUp', 'dpadDown', 'dpadLeft', 'dpadRight'] },
  { title: 'System', ids: ['view', 'menu'] }
];

const DPAD_GLYPH = { dpadUp: 'Up', dpadDown: 'Down', dpadLeft: 'Left', dpadRight: 'Right', lstick: 'L stick', rstick: 'R stick', lclick: 'LS', rclick: 'RS' };

export function glyphFor(input, kind) {
  const g = GLYPHS[kind] || GLYPHS.xbox;
  return input.key ? g[input.key] : (DPAD_GLYPH[input.id] || input.label);
}

export function artFor(id, kind) {
  const dir = ART_DIR[kind] ?? 'xbox';
  return dir ? `assets/pad/${dir}/${id}.png` : '';
}

export const LAYER_ACTION = 'Hold for second layer';

export const ACTIONS = {
  Mouse: ['Left click', 'Right click', 'Middle click', 'Move cursor', 'Scroll'],
  Keyboard: [],
  Numpad: [],
  'Media and volume': ['Play / pause', 'Next track', 'Previous track', 'Volume up', 'Volume down', 'Mute'],
  System: ['Show keyboard', 'Mouse mode on / off', 'Switch profile', LAYER_ACTION, 'Precision cursor']
};

// label, flex weight, modifier id
export const KEY_ROWS = [
  [['Esc', 1], ['F1', 1], ['F2', 1], ['F3', 1], ['F4', 1], ['F5', 1], ['F6', 1], ['F7', 1], ['F8', 1], ['F9', 1], ['F10', 1], ['F11', 1], ['F12', 1]],
  [['`', 1], ['1', 1], ['2', 1], ['3', 1], ['4', 1], ['5', 1], ['6', 1], ['7', 1], ['8', 1], ['9', 1], ['0', 1], ['-', 1], ['=', 1], ['Backspace', 2]],
  [['Tab', 1.5], ['Q', 1], ['W', 1], ['E', 1], ['R', 1], ['T', 1], ['Y', 1], ['U', 1], ['I', 1], ['O', 1], ['P', 1], ['[', 1], [']', 1], ['\\', 1]],
  [['Caps', 1.8], ['A', 1], ['S', 1], ['D', 1], ['F', 1], ['G', 1], ['H', 1], ['J', 1], ['K', 1], ['L', 1], [';', 1], ["'", 1], ['Enter', 2]],
  [['Shift', 2.2, 'shift'], ['Z', 1], ['X', 1], ['C', 1], ['V', 1], ['B', 1], ['N', 1], ['M', 1], [',', 1], ['.', 1], ['/', 1], ['Up', 1], ['Del', 1.2]],
  [['Ctrl', 1.4, 'ctrl'], ['Win', 1.2, 'win'], ['Alt', 1.2, 'alt'], ['Space', 4.5], ['Left', 1], ['Down', 1], ['Right', 1], ['Home', 1.2], ['End', 1.2]]
];

// label, grid-area (row/col/rowEnd/colEnd)
export const NUMPAD_KEYS = [
  ['Num Lock', '1/1/2/2'], ['/', '1/2/2/3'], ['*', '1/3/2/4'], ['-', '1/4/2/5'],
  ['7', '2/1/3/2'], ['8', '2/2/3/3'], ['9', '2/3/3/4'], ['+', '2/4/4/5'],
  ['4', '3/1/4/2'], ['5', '3/2/4/3'], ['6', '3/3/4/4'],
  ['1', '4/1/5/2'], ['2', '4/2/5/3'], ['3', '4/3/5/4'], ['Enter', '4/4/6/5'],
  ['0', '5/1/6/3'], ['.', '5/3/6/4']
];

// Longer names for keys whose cap label is short.
export const KEY_NAMES = { Up: 'Up arrow', Down: 'Down arrow', Left: 'Left arrow', Right: 'Right arrow', Caps: 'Caps Lock', Del: 'Delete' };

export const MODIFIERS = [['Ctrl', 'ctrl'], ['Alt', 'alt'], ['Shift', 'shift'], ['Win', 'win']];

// Every action the Keyboard and Numpad pickers can produce, for search.
export function searchableActions() {
  const keys = [];
  KEY_ROWS.forEach((row) => row.forEach((k) => { if (!k[2]) keys.push(KEY_NAMES[k[0]] || k[0]); }));
  const numpad = NUMPAD_KEYS.map((k) => (k[0] === 'Num Lock' ? 'Num Lock' : 'Num ' + k[0]));
  return [
    ...ACTIONS.Mouse.map((a) => ({ a, c: 'Mouse' })),
    ...keys.map((a) => ({ a, c: 'Keyboard' })),
    ...numpad.map((a) => ({ a, c: 'Numpad' })),
    ...ACTIONS['Media and volume'].map((a) => ({ a, c: 'Media and volume' })),
    ...ACTIONS.System.map((a) => ({ a, c: 'System' }))
  ];
}

export function limitsDialog(appName) {
  return {
    title: 'What ' + appName + ' can’t do',
    closeLabel: 'Close',
    body: 'Administrator windows such as Task Manager only accept input from apps running as administrator, so the controller does nothing there unless you run ' + appName + ' as administrator too. UAC prompts and the lock screen are off limits to every app, including this one. If another application also responds to your controller, a single press can trigger duplicate actions. ' + appName + ' detects this and displays a warning. And the on-screen keyboard is Windows’ own: ' + appName + ' only opens and closes it, so its look can’t be changed here.',
    items: []
  };
}
