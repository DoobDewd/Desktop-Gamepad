import React, { useEffect, useState } from 'react';
import { ACCENT, GLYPHS, INPUTS, glyphFor } from './data.js';
import { Button, Switch, clickable } from './common.jsx';
import { pauseNav } from './padnav.js';

const KEEP_FOR_CUSTOM = ['lstick', 'a', 'view'];

export default function Setup({ settings, update, status, kind, appName, lastInput, onDone }) {
  const [step, setStep] = useState(1);
  const [preset, setPreset] = useState('browse');
  const pad = status.controller;
  const connected = !!(pad && pad.connected);

  const steps = {
    1: { title: 'Connect your controller', body: 'Turn it on, or plug it in. ' + appName + ' does nothing at all until it sees one.', next: connected ? 'Next' : 'Skip for now', back: 'Set up later' },
    2: { title: 'Give it a try', body: 'Press a few buttons and push the sticks. Every input you press shows up here, so you can check the controller is being read.', next: 'Next', back: 'Back' },
    3: { title: 'Pick a starting point', body: 'Both are fully editable afterwards.', next: 'Finish', back: 'Back' }
  };
  const s = steps[step];
  const menuName = (GLYPHS[kind] || GLYPHS.xbox).menu || 'Menu';

  // Every press is a test on the "try it" step, so the controller does not move around the screen there. Menu moves on.
  useEffect(() => (step === 2 ? pauseNav((key) => { if (key === 'menu') setStep(3); }) : undefined), [step]);

  const finish = () => {
    if (preset === 'custom') {
      // "Pointer and clicking only": keep the cursor stick and the two clicks, clear everything else.
      update((st) => {
        Object.values(st.profiles).forEach((map) => Object.keys(map).forEach((id) => {
          if (!KEEP_FOR_CUSTOM.includes(id)) map[id] = { base: {}, layer: {} };
          else map[id].layer = {};
        }));
      });
    }
    onDone(preset === 'custom' ? 'buttons' : 'home');
  };

  const hit = lastInput && INPUTS.find((i) => i.id === lastInput.id);

  return (
    <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '44px 40px 32px', overflow: 'auto' }}>
      <div style={{ width: '100%', maxWidth: 620 }}>
        <div style={{ display: 'flex', gap: 8, marginBottom: 28 }}>
          {[1, 2, 3].map((n) => <div key={n} style={{ height: 3, flex: 1, borderRadius: 2, background: n <= step ? ACCENT : 'rgba(255,255,255,.12)' }} />)}
        </div>
        <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)', textTransform: 'uppercase', letterSpacing: '.08em', marginBottom: 10 }}>Step {step} of 3</div>
        <div className="display" style={{ fontSize: 28, lineHeight: 1.2, fontWeight: 600, marginBottom: 10 }}>{s.title}</div>
        <div style={{ fontSize: 14, lineHeight: 1.55, color: 'rgba(255,255,255,.786)', maxWidth: '52ch', marginBottom: 26 }}>{s.body}</div>

        {step === 1 && (
          <>
            <div style={{ border: '1px solid rgba(255,255,255,.08)', background: '#2b2b2b', borderRadius: 8, padding: '18px 20px', display: 'flex', alignItems: 'center', gap: 16 }}>
              <div style={{ width: 44, height: 44, borderRadius: 8, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', background: connected ? 'rgba(96,205,255,.12)' : 'rgba(255,255,255,.06)', color: connected ? ACCENT : 'rgba(255,255,255,.4)', fontSize: 11, fontFamily: 'ui-monospace,Consolas,monospace' }}>pad</div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 14, fontWeight: 600 }}>{connected ? pad.name : 'Looking for a controller…'}</div>
                <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)', marginTop: 2 }}>{connected ? pad.connection : 'Nothing found yet'}</div>
              </div>
              <div style={{ fontSize: 12, padding: '4px 10px', borderRadius: 12, background: connected ? 'rgba(108,203,95,.15)' : 'rgba(255,255,255,.06)', color: connected ? '#6ccb5f' : 'rgba(255,255,255,.55)' }}>
                {connected ? (pad.battery != null ? pad.battery + '%' : 'Connected') : 'Waiting'}
              </div>
            </div>
            <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)', marginTop: 12, lineHeight: 1.5 }}>Bluetooth or USB both work. Nothing runs while no controller is connected.</div>
          </>
        )}

        {step === 2 && (
          <>
            <div style={{ border: '1px solid rgba(255,255,255,.08)', borderRadius: 8, background: '#2b2b2b', padding: '18px 20px', maxWidth: 420, display: 'flex', alignItems: 'center', gap: 14 }}>
              <div style={{ width: 46, height: 46, borderRadius: 8, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', background: hit ? 'rgba(108,203,95,.16)' : 'rgba(255,255,255,.05)', color: hit ? '#6ccb5f' : 'rgba(255,255,255,.4)', fontSize: 12, fontWeight: 700 }}>{hit ? glyphFor(hit, kind) : '—'}</div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 14, fontWeight: 600 }}>{hit ? hit.label : 'Waiting for a button'}</div>
                <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)', marginTop: 2 }}>{hit ? 'Read straight from the controller' : 'Press anything on the pad, or push a stick'}</div>
              </div>
            </div>
            <div style={{ display: 'flex', gap: 10, marginTop: 14, alignItems: 'center' }}>
              <div style={{ width: 8, height: 8, borderRadius: '50%', background: '#6ccb5f', flex: 'none' }} />
              <div style={{ fontSize: 13, color: 'rgba(255,255,255,.786)' }}>Press any button — it shows up here.</div>
            </div>
            <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)', marginTop: 10 }}>On the controller, press {menuName} to continue.</div>
          </>
        )}

        {step === 3 && (
          <>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(240px,1fr))', gap: 12 }}>
              {[
                { id: 'browse', name: 'Browse and media', detail: 'Pointer, smooth scrolling, tab switching on the bumpers, volume on the D-pad, and the touch keyboard opening by itself.' },
                { id: 'custom', name: 'Custom', detail: 'Pointer and clicking only. Everything else is yours to map.' }
              ].map((p) => {
                const on = preset === p.id;
                return (
                  <div key={p.id} {...clickable(() => setPreset(p.id))} className="hov-card"
                    style={{ cursor: 'pointer', borderRadius: 8, padding: '16px 18px', background: '#2b2b2b', border: '1px solid ' + (on ? ACCENT : 'rgba(255,255,255,.08)') }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      <div style={{ width: 16, height: 16, borderRadius: '50%', border: '1px solid ' + (on ? ACCENT : 'rgba(255,255,255,.4)'), background: on ? ACCENT : 'transparent', flex: 'none' }} />
                      <div style={{ fontSize: 14, fontWeight: 600 }}>{p.name}</div>
                    </div>
                    <div style={{ fontSize: 12, lineHeight: 1.5, color: 'rgba(255,255,255,.62)', marginTop: 8 }}>{p.detail}</div>
                  </div>
                );
              })}
            </div>
            <div {...clickable(() => update((st) => { st.startWithWindows = !st.startWithWindows; }))}
              style={{ cursor: 'pointer', marginTop: 18, display: 'flex', alignItems: 'center', gap: 12, padding: '14px 16px', borderRadius: 8, background: '#2b2b2b', border: '1px solid rgba(255,255,255,.07)' }}>
              <Switch on={settings.startWithWindows} />
              <div style={{ fontSize: 14 }}>Start with Windows</div>
            </div>
          </>
        )}

        <div style={{ display: 'flex', gap: 10, marginTop: 28 }}>
          <Button primary onClick={() => (step < 3 ? setStep(step + 1) : finish())} style={{ fontSize: 14, padding: '8px 22px' }}>{s.next}</Button>
          <Button onClick={() => (step > 1 ? setStep(step - 1) : onDone('home'))} style={{ fontSize: 14, padding: '8px 18px' }}>{s.back}</Button>
        </div>
      </div>
    </div>
  );
}
