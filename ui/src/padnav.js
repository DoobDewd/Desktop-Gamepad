// Controller navigation for Desktop Gamepad's own window (BUILD_NOTES §2). While this window is in front, DesktopGamepad.exe sends
// "nav" messages instead of moving the cursor: up / down / left / right move a highlight between controls, select
// presses the highlighted one, back closes whatever is open. It works on the plain DOM, so every screen gets it
// without extra wiring.
import { send } from './bridge.js';

const FOCUSABLE = '[role="button"], button, select, input, textarea, a[href], [tabindex]:not([tabindex="-1"])';

let current = null;   // the highlighted element
let lastPoint = null; // its centre, to carry on from when the page changes under it
let opener = null;    // what was highlighted before a dialog opened, to return to when it closes
let lastNavAt = 0;
let pauseHandler = null;
let chooser = null;   // the open option list for a <select>

/** While set, nav messages go to this handler instead (the setup screen's "try it" step, where every press is a test). */
export function pauseNav(handler) {
  pauseHandler = handler;
  return () => { if (pauseHandler === handler) pauseHandler = null; };
}

/** True while something covers the page, so switching sections (LB / RB) waits. */
export function navBlocked() {
  return !!pauseHandler || !!document.querySelector('[data-modal]');
}

export function handleNav(key) {
  lastNavAt = Date.now();
  if (pauseHandler) { pauseHandler(key); return; }
  if (current && current.isConnected) current.setAttribute('data-padfocus', '');
  if (key === 'up' || key === 'down' || key === 'left' || key === 'right') move(key);
  else if (key === 'select') activate();
  else if (key === 'back') back();
}

// ---- Finding things ----

function scope() {
  if (chooser) return chooser.list;
  const modals = document.querySelectorAll('[data-modal]');
  return modals.length ? modals[modals.length - 1] : document.body;
}

function targets(root) {
  return Array.from(root.querySelectorAll(FOCUSABLE)).filter((el) => {
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  });
}

/** Where the highlight starts: the page content first, so the sidebar is one press to the left. */
function startElement(root) {
  const main = root === document.body && document.querySelector('main');
  return (main && targets(main)[0]) || targets(root)[0] || null;
}

function nearestTo(root, point) {
  let best = null, bestDistance = Infinity;
  for (const el of targets(root)) {
    const r = el.getBoundingClientRect();
    const d = Math.hypot(r.left + r.width / 2 - point.x, r.top + r.height / 2 - point.y);
    if (d < bestDistance) { best = el; bestDistance = d; }
  }
  return best;
}

/** The closest control in a direction, preferring ones lined up with the current one. */
function inDirection(root, dir) {
  const a = current.getBoundingClientRect();
  const ax = a.left + a.width / 2, ay = a.top + a.height / 2;
  const vertical = dir === 'up' || dir === 'down';
  const sign = dir === 'down' || dir === 'right' ? 1 : -1;
  // Up and down stay inside the sidebar or the page; only left and right cross between them.
  const area = (el) => el.closest('nav, main');
  const currentArea = area(current);
  // Controls lined up with the current one (in its row for left / right, its column for up / down) win over closer
  // ones off to the side; only when nothing is lined up does the nearest control in that direction count.
  let lined = null, linedScore = Infinity, any = null, anyScore = Infinity;
  for (const el of targets(root)) {
    if (el === current || el.contains(current) || current.contains(el)) continue;
    if (vertical && area(el) !== currentArea) continue;
    const b = el.getBoundingClientRect();
    const bx = b.left + b.width / 2, by = b.top + b.height / 2;
    if ((vertical ? by - ay : bx - ax) * sign <= 1) continue;
    const gap = Math.max(0, vertical ? (sign > 0 ? b.top - a.bottom : a.top - b.bottom) : (sign > 0 ? b.left - a.right : a.left - b.right));
    const overlap = vertical ? Math.min(a.right, b.right) - Math.max(a.left, b.left) : Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
    if (overlap > 0 && gap < linedScore) { lined = el; linedScore = gap; }
    const score = gap + (overlap > 0 ? 0 : Math.abs(vertical ? bx - ax : by - ay) * 3);
    if (score < anyScore) { any = el; anyScore = score; }
  }
  return lined || any;
}

// ---- Moving and pressing ----

function setCurrent(el) {
  if (current && current !== el) current.removeAttribute('data-padfocus');
  current = el;
  if (!el) return;
  el.setAttribute('data-padfocus', '');
  el.focus({ preventScroll: true });
  el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  const r = el.getBoundingClientRect();
  lastPoint = { x: r.left + r.width / 2, y: r.top + r.height / 2 };
}

/** Makes sure something inside the current scope is highlighted. Returns false when it had to place the highlight. */
function ensureCurrent(root) {
  if (current && current.isConnected && root.contains(current)) return true;
  if (root !== document.body && current && current.isConnected) opener = current;
  if (root === document.body && opener && opener.isConnected) {
    setCurrent(opener);
    opener = null;
    return false;
  }
  const replaced = current && !current.isConnected && lastPoint;
  setCurrent(replaced ? nearestTo(root, lastPoint) : startElement(root));
  return false;
}

/** After a press opens or closes a dialog, move the highlight into it or back out. */
function settleSoon() {
  setTimeout(() => {
    const root = scope();
    if (!(current && current.isConnected && root.contains(current))) ensureCurrent(root);
  }, 60);
}

function move(dir) {
  const root = scope();
  if (!ensureCurrent(root)) return;
  if (current.tagName === 'INPUT' && current.type === 'range' && (dir === 'left' || dir === 'right')) {
    stepRange(current, dir === 'right' ? 1 : -1);
    return;
  }
  const next = inDirection(root, dir);
  if (next) setCurrent(next);
}

function activate() {
  if (!ensureCurrent(scope())) return;
  const el = current;
  if (el.tagName === 'SELECT') { openChooser(el); return; }
  if (el.tagName === 'INPUT' && el.type === 'range') return;
  if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {
    el.focus();
    send({ type: 'showKeyboard' });
    return;
  }
  el.click();
  settleSoon();
}

function back() {
  if (chooser) { closeChooser(); return; }
  const active = document.activeElement;
  const typing = active && (active.tagName === 'TEXTAREA' || (active.tagName === 'INPUT' && active.type !== 'range'));
  if (typing) send({ type: 'hideKeyboard' });
  // The screens already close dialogs and cancel edits on Escape.
  const target = active && active !== document.body ? active : document;
  target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true }));
  settleSoon();
}

/** Sets the value of a React-controlled input the way typing would, so its onChange runs. */
function setValue(el, value, eventName) {
  const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value').set;
  setter.call(el, value);
  el.dispatchEvent(new Event(eventName, { bubbles: true }));
}

function stepRange(el, delta) {
  const min = Number(el.min || 0), max = Number(el.max || 100), step = Number(el.step || 1);
  const value = Math.min(max, Math.max(min, Number(el.value) + delta * step));
  if (String(value) !== el.value) setValue(el, String(value), 'input');
}

// ---- Option list for dropdowns ----
// A page cannot drive Windows' own dropdown list, so the controller gets a list drawn here instead.

function openChooser(select) {
  const backdrop = document.createElement('div');
  backdrop.className = 'pp-chooser-backdrop';
  backdrop.setAttribute('data-modal', '');
  const list = document.createElement('div');
  list.className = 'pp-chooser';
  Array.from(select.options).forEach((option) => {
    const item = document.createElement('div');
    item.className = 'pp-chooser-item' + (option.selected ? ' on' : '');
    item.textContent = option.textContent;
    item.setAttribute('role', 'button');
    item.tabIndex = 0;
    item.addEventListener('click', () => {
      closeChooser();
      if (select.value !== option.value) setValue(select, option.value, 'change');
    });
    list.appendChild(item);
  });
  backdrop.addEventListener('mousedown', (e) => { if (e.target === backdrop) closeChooser(); });
  backdrop.appendChild(list);
  document.body.appendChild(backdrop);

  const r = select.getBoundingClientRect();
  list.style.minWidth = r.width + 'px';
  const height = list.offsetHeight;
  const fitsBelow = r.bottom + 4 + height <= window.innerHeight - 8;
  list.style.left = Math.max(8, Math.min(r.left, window.innerWidth - list.offsetWidth - 8)) + 'px';
  list.style.top = (fitsBelow ? r.bottom + 4 : Math.max(8, r.top - 4 - height)) + 'px';

  chooser = { backdrop, list, select };
  setCurrent(list.querySelector('.on') || list.firstChild);
}

function closeChooser() {
  if (!chooser) return;
  const { backdrop, select } = chooser;
  chooser = null;
  backdrop.remove();
  if (select.isConnected) setCurrent(select);
}

// ---- Handing back to the mouse ----

if (typeof window !== 'undefined') {
  let mouseAt = null;
  window.addEventListener('mousemove', (e) => {
    const moved = !mouseAt || Math.abs(e.screenX - mouseAt.x) + Math.abs(e.screenY - mouseAt.y) > 3;
    mouseAt = { x: e.screenX, y: e.screenY };
    // Desktop Gamepad moves the pointer itself when it hides it, right after a controller press: that is not the mouse.
    if (!moved || !current || Date.now() - lastNavAt < 600) return;
    current.removeAttribute('data-padfocus');
    if (document.activeElement === current && current.tagName !== 'INPUT' && current.tagName !== 'TEXTAREA') current.blur();
  }, true);
  window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && chooser) closeChooser(); }, true);
}
