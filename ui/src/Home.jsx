import React from 'react';
import { limitsDialog } from './data.js';
import { Button, Divider, PageTitle, ToggleRow, card, clickable, soft, muted } from './common.jsx';

export default function Home({ settings, update, status, appName, setDialog }) {
  const pad = status.controller;
  const connected = !!(pad && pad.connected);

  const banners = [];
  if (status.steamConflict) banners.push({
    title: 'Controller input conflict',
    body: 'Another application is also responding to your controller, which can cause duplicate actions. Disable controller input in that application, or turn off Mouse in ' + appName + '.',
    action: 'How to fix', bg: 'rgba(224,178,60,.1)', border: 'rgba(224,178,60,.35)', dot: '#e0b23c',
    onAction: () => setDialog({
      title: 'Resolve the controller input conflict',
      body: 'Another application on this PC is responding to the same controller input as ' + appName + ', so a single press can trigger two actions. To resolve this, disable controller input in that application, or turn off Mouse in ' + appName + ' while that application is in use.',
      items: [], closeLabel: 'Got it'
    })
  });
  if (status.adminWindow) banners.push({
    title: 'This window ignores controller input',
    body: 'Task Manager and other administrator windows only accept input from apps running as administrator. Restart ' + appName + ' as administrator to control them.',
    action: 'Explain', bg: 'rgba(255,255,255,.04)', border: 'rgba(255,255,255,.1)', dot: 'rgba(255,255,255,.5)',
    onAction: () => setDialog(limitsDialog(appName))
  });

  const battery = connected ? pad.battery : null;
  const batteryColor = battery == null ? 'rgba(255,255,255,.55)' : battery <= 10 ? '#e06c6c' : battery <= 20 ? '#e0b23c' : '#6ccb5f';

  return (
    <div>
      <PageTitle style={{ margin: 0 }}>{appName}</PageTitle>
      <div style={{ fontSize: 13, color: muted, margin: '4px 0 22px' }}>Use your controller as a mouse and keyboard.</div>

      {banners.map((w, i) => (
        <div key={i} style={{ display: 'flex', gap: 12, alignItems: 'flex-start', padding: '14px 16px', borderRadius: 8, marginBottom: 12, background: w.bg, border: '1px solid ' + w.border }}>
          <div style={{ width: 6, height: 6, borderRadius: '50%', marginTop: 6, flex: 'none', background: w.dot }} />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 13, fontWeight: 600 }}>{w.title}</div>
            <div style={{ fontSize: 12, lineHeight: 1.5, color: 'rgba(255,255,255,.72)', marginTop: 3 }}>{w.body}</div>
          </div>
          <Button onClick={w.onAction} style={{ fontSize: 12, padding: '5px 12px' }}>{w.action}</Button>
        </div>
      ))}

      {connected ? (
        <div style={{ ...card, padding: '18px 20px', display: 'flex', alignItems: 'center', gap: 16 }}>
          <div style={{ width: 46, height: 46, borderRadius: 8, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'rgba(96,205,255,.12)', color: '#60cdff', fontSize: 11, fontFamily: 'ui-monospace,Consolas,monospace' }}>pad</div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 15, fontWeight: 600 }}>{pad.name}</div>
            <div style={{ fontSize: 12, color: muted, marginTop: 3 }}>{pad.connection}</div>
          </div>
          {battery != null && (
            <div style={{ textAlign: 'right', flex: 'none' }}>
              <div style={{ fontSize: 15, fontWeight: 600, color: batteryColor }}>{battery}%</div>
              <div style={{ width: 86, height: 4, borderRadius: 2, background: 'rgba(255,255,255,.12)', marginTop: 6, overflow: 'hidden' }}>
                <div style={{ height: '100%', borderRadius: 2, background: batteryColor, width: Math.max(2, battery) + '%' }} />
              </div>
            </div>
          )}
        </div>
      ) : (
        <div style={{ border: '1px dashed rgba(255,255,255,.16)', borderRadius: 8, padding: '26px 20px', textAlign: 'center', background: 'rgba(255,255,255,.02)' }}>
          <div style={{ fontSize: 15, fontWeight: 600 }}>No controller connected</div>
          <div style={{ fontSize: 13, lineHeight: 1.55, color: soft, margin: '8px auto 0', maxWidth: '44ch' }}>{appName} is asleep until a controller appears. Turn yours on, or pair it in Settings → Bluetooth &amp; devices.</div>
        </div>
      )}

      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start', padding: '14px 16px', borderRadius: 8, marginTop: 12, background: 'rgba(108,203,95,.08)', border: '1px solid rgba(108,203,95,.3)' }}>
        <div style={{ flex: 'none', fontSize: 11, fontWeight: 700, letterSpacing: '.04em', color: '#6ccb5f', marginTop: 1 }}>TIP</div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 13, fontWeight: 600 }}>Works great with Xbox mode</div>
          <div style={{ fontSize: 12, lineHeight: 1.5, color: 'rgba(255,255,255,.72)', marginTop: 3 }}>
            Xbox mode gives your PC a console-style home screen. Turn it on in Settings → Gaming → Xbox mode.
          </div>
        </div>
      </div>

      <div style={{ ...card, overflow: 'hidden', marginTop: 12 }}>
        <ToggleRow first on={settings.mouseMode === 'on'} label="Mouse"
          help={settings.mouseMode === 'on' ? 'The controller moves the cursor and clicks.' : 'The controller does nothing on the desktop until you turn this back on.'}
          onToggle={() => update((s) => { s.mouseMode = s.mouseMode === 'on' ? 'off' : 'on'; })} />
        <ToggleRow on={settings.keyboard} label="Keyboard" help="Opens the keyboard when you click a text box."
          onToggle={() => update((s) => { s.keyboard = !s.keyboard; })} />
      </div>

      <div style={{ ...card, padding: 20, marginTop: 12 }}>
        <Slider label="Cursor speed" value={settings.cursorSpeed} onChange={(v) => update((s) => { s.cursorSpeed = v; })} />
        <Divider />
        <Slider label="Scroll speed" value={settings.scrollSpeed} onChange={(v) => update((s) => { s.scrollSpeed = v; })} />
      </div>

      <div style={{ marginTop: 20, fontSize: 12, color: muted }}>
        Some Windows screens can't be controlled by any app. <a {...clickable(() => setDialog(limitsDialog(appName)))}>What {appName} can't do</a>
      </div>
    </div>
  );
}

function Slider({ label, value, onChange }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
      <div style={{ flex: 1, minWidth: 0, fontSize: 14 }}>{label}</div>
      <input type="range" min="1" max="10" step="1" value={value} onChange={(e) => onChange(+e.target.value)} style={{ flex: 'none', width: 170, height: 18 }} />
      <div style={{ flex: 'none', width: 18, textAlign: 'right', fontSize: 13, color: 'rgba(255,255,255,.7)', fontVariantNumeric: 'tabular-nums' }}>{value}</div>
    </div>
  );
}
