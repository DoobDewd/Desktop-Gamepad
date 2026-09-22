import React, { useEffect, useState } from 'react';
import { ACTIONS, KEY_NAMES, KEY_ROWS, MODIFIERS, NUMPAD_KEYS, searchableActions } from './data.js';
import { Button, clickable } from './common.jsx';

const NO_MODS = { ctrl: false, alt: false, shift: false, win: false };

export default function Picker({ subtitle, current, onPick, onClose }) {
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('Mouse');
  const [mods, setMods] = useState(NO_MODS);
  const [recording, setRecording] = useState(false);
  const [recorded, setRecorded] = useState('');

  useEffect(() => {
    if (!recording) return undefined;
    const onKey = (e) => {
      e.preventDefault();
      const parts = [];
      if (e.ctrlKey) parts.push('Ctrl');
      if (e.altKey) parts.push('Alt');
      if (e.shiftKey) parts.push('Shift');
      if (e.metaKey) parts.push('Win');
      const k = e.key;
      if (!['Control', 'Alt', 'Shift', 'Meta'].includes(k)) parts.push(keyName(e));
      setRecorded(parts.join('+'));
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [recording]);

  useEffect(() => {
    const onEsc = (e) => { if (e.key === 'Escape' && !recording) onClose(); };
    window.addEventListener('keydown', onEsc);
    return () => window.removeEventListener('keydown', onEsc);
  }, [recording, onClose]);

  const q = query.trim().toLowerCase();
  const showKeyboard = !q && category === 'Keyboard';
  const showNumpad = !q && category === 'Numpad';
  const list = q
    ? searchableActions().filter((x) => x.a.toLowerCase().includes(q))
    : (ACTIONS[category] || []).map((a) => ({ a, c: category }));
  const prefix = MODIFIERS.filter(([, id]) => mods[id]).map(([label]) => label);
  const combo = (name) => onPick(prefix.concat([name]).join('+'));

  return (
    <div data-modal="" style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 32 }}>
      <div style={{ width: '100%', maxWidth: 840, maxHeight: '84vh', display: 'flex', flexDirection: 'column', borderRadius: 9, background: '#2b2b2b', border: '1px solid rgba(255,255,255,.1)', boxShadow: '0 40px 80px rgba(0,0,0,.6)', overflow: 'hidden', animation: 'pp-pop .14s ease-out' }}>
        <div style={{ padding: '20px 22px 14px', borderBottom: '1px solid rgba(255,255,255,.07)' }}>
          <div style={{ fontSize: 19, fontWeight: 600 }}>Choose an action</div>
          <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)', marginTop: 4 }}>{subtitle}</div>
          <input autoFocus value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search actions"
            style={{ marginTop: 14, width: '100%', font: 'inherit', fontSize: 13, color: '#fff', background: 'rgba(255,255,255,.05)', border: '1px solid rgba(255,255,255,.1)', borderBottom: '1px solid rgba(255,255,255,.3)', borderRadius: 5, padding: '8px 11px', outline: 'none' }} />
        </div>

        <div style={{ flex: 1, minHeight: 0, display: 'flex' }}>
          <div style={{ width: 186, flex: 'none', borderRight: '1px solid rgba(255,255,255,.07)', padding: '8px 6px', overflow: 'auto' }}>
            {Object.keys(ACTIONS).map((c) => {
              const on = !q && category === c;
              return (
                <div key={c} {...clickable(() => { setCategory(c); setQuery(''); })} className="hov-nav"
                  style={{ cursor: 'pointer', fontSize: 13, padding: '9px 11px', borderRadius: 5, background: on ? 'rgba(255,255,255,.08)' : 'transparent', color: on ? '#fff' : 'rgba(255,255,255,.75)', marginBottom: 2 }}>{c}</div>
              );
            })}
          </div>

          <div style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: 8 }}>
            {showKeyboard && (
              <div style={{ padding: '4px 4px 2px' }}>
                <Hint text="Click a key. Add Ctrl, Alt, Shift or Win first for a combination." prefix={prefix} />
                <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  {KEY_ROWS.map((row, r) => (
                    <div key={r} style={{ display: 'flex', gap: 4 }}>
                      {row.map((k) => {
                        const mod = k[2], on = mod && mods[mod];
                        return (
                          <div key={k[0]} {...clickable(() => (mod ? setMods({ ...mods, [mod]: !mods[mod] }) : combo(KEY_NAMES[k[0]] || k[0])))} className="hov-key"
                            style={{ flex: k[1] + ' 1 0', minWidth: 0, height: 30, display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: 4, cursor: 'pointer', fontSize: 11, background: on ? 'rgba(96,205,255,.22)' : 'rgba(255,255,255,.05)', border: '1px solid ' + (on ? 'rgba(96,205,255,.6)' : 'rgba(255,255,255,.09)'), color: on ? '#cfeeff' : 'rgba(255,255,255,.85)', overflow: 'hidden' }}>
                            {k[0]}
                          </div>
                        );
                      })}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {showNumpad && (
              <div style={{ padding: '4px 4px 2px' }}>
                <Hint text="Click a numpad key. Add a modifier first for a combination." prefix={prefix} />
                <div style={{ display: 'flex', gap: 6, marginBottom: 14, flexWrap: 'wrap' }}>
                  {MODIFIERS.map(([label, id]) => (
                    <div key={id} {...clickable(() => setMods({ ...mods, [id]: !mods[id] }))} className="hov-key"
                      style={{ cursor: 'pointer', fontSize: 12, padding: '6px 14px', borderRadius: 5, background: mods[id] ? 'rgba(96,205,255,.22)' : 'rgba(255,255,255,.05)', border: '1px solid ' + (mods[id] ? 'rgba(96,205,255,.6)' : 'rgba(255,255,255,.09)'), color: mods[id] ? '#cfeeff' : 'rgba(255,255,255,.85)' }}>{label}</div>
                  ))}
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,44px)', gridTemplateRows: 'repeat(5,38px)', gap: 5 }}>
                  {NUMPAD_KEYS.map((k) => (
                    <div key={k[0]} {...clickable(() => combo(k[0] === 'Num Lock' ? 'Num Lock' : 'Num ' + k[0]))} className="hov-key"
                      style={{ gridArea: k[1], display: 'flex', alignItems: 'center', justifyContent: 'center', textAlign: 'center', lineHeight: 1.15, padding: '0 3px', borderRadius: 4, cursor: 'pointer', fontSize: k[0].length > 4 ? 10 : 12, background: 'rgba(255,255,255,.05)', border: '1px solid rgba(255,255,255,.09)', color: 'rgba(255,255,255,.85)' }}>{k[0]}</div>
                  ))}
                </div>
                <div style={{ fontSize: 11, color: 'rgba(255,255,255,.45)', marginTop: 10, lineHeight: 1.5 }}>Numpad keys are separate from the number row, so apps can tell them apart.</div>
              </div>
            )}

            {!showKeyboard && !showNumpad && list.map((x) => (
              <div key={x.c + x.a} {...clickable(() => onPick(x.a))} className="hov-item"
                style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 10, padding: '11px 12px', borderRadius: 6, marginBottom: 2, background: x.a === current ? 'rgba(96,205,255,.12)' : 'transparent' }}>
                <div style={{ flex: 1, minWidth: 0, fontSize: 13 }}>{x.a}</div>
                <div style={{ fontSize: 11, color: 'rgba(255,255,255,.4)', flex: 'none' }}>{q ? x.c : ''}</div>
              </div>
            ))}
            {!showKeyboard && !showNumpad && list.length === 0 && (
              <div style={{ padding: '26px 12px', fontSize: 13, color: 'rgba(255,255,255,.5)', textAlign: 'center' }}>Nothing matches “{query}”.</div>
            )}
          </div>
        </div>

        <div style={{ flex: 'none', padding: '14px 22px', borderTop: '1px solid rgba(255,255,255,.07)', display: 'flex', alignItems: 'center', gap: 12, background: 'rgba(0,0,0,.15)' }}>
          <div {...clickable(() => { setRecording(true); setRecorded(''); })}
            style={{ cursor: 'pointer', fontSize: 13, padding: '8px 14px', borderRadius: 5, background: recording ? 'rgba(224,178,60,.16)' : 'rgba(255,255,255,.06)', border: '1px solid ' + (recording ? 'rgba(224,178,60,.5)' : 'rgba(255,255,255,.09)'), color: recording ? '#f0cd7a' : '#fff' }}>
            {recording ? 'Press keys now…' : 'Record a key combination'}
          </div>
          <div style={{ flex: 1, minWidth: 0, fontSize: 12, color: 'rgba(255,255,255,.55)' }}>
            {recorded ? 'Captured: ' + recorded : recording ? 'Hold the modifiers and press the key.' : ''}
          </div>
          {recorded && <Button primary onClick={() => onPick(recorded)}>Use this</Button>}
          <Button onClick={onClose}>Cancel</Button>
        </div>
      </div>
    </div>
  );
}

function Hint({ text, prefix }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', marginBottom: 10 }}>
      <div style={{ fontSize: 12, color: 'rgba(255,255,255,.545)' }}>{text}</div>
      <div style={{ flex: 1, minWidth: 0 }} />
      <div style={{ fontSize: 13, fontWeight: 600, color: '#60cdff', fontFamily: 'ui-monospace,Consolas,monospace', whiteSpace: 'nowrap' }}>{prefix.length ? prefix.join('+') + '+…' : 'click a key'}</div>
    </div>
  );
}

/** A recorded key as the same name the on-screen keyboard uses. */
function keyName(e) {
  const byCode = { ArrowUp: 'Up arrow', ArrowDown: 'Down arrow', ArrowLeft: 'Left arrow', ArrowRight: 'Right arrow', Delete: 'Delete', CapsLock: 'Caps Lock', Escape: 'Esc', ' ': 'Space' };
  if (e.code && e.code.startsWith('Numpad')) {
    const rest = e.code.slice(6);
    const map = { Add: '+', Subtract: '-', Multiply: '*', Divide: '/', Decimal: '.', Enter: 'Enter' };
    return 'Num ' + (map[rest] || rest);
  }
  if (byCode[e.key]) return byCode[e.key];
  return e.key.length === 1 ? e.key.toUpperCase() : e.key;
}
