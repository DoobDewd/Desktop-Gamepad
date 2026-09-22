import React, { useEffect } from 'react';
import { ACCENT } from './data.js';

export const card = { background: '#2b2b2b', border: '1px solid rgba(255,255,255,.07)', borderRadius: 8 };
export const muted = 'rgba(255,255,255,.545)';
export const soft = 'rgba(255,255,255,.62)';

/** Enter/Space act like a click, so every clickable element works from the keyboard and the controller. */
export function clickable(onClick) {
  return {
    role: 'button',
    tabIndex: 0,
    onClick,
    onKeyDown: (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick(e); } }
  };
}

export function PageTitle({ children, style }) {
  return <div className="display" style={{ fontSize: 28, fontWeight: 600, ...style }}>{children}</div>;
}

export function Divider() {
  return <div style={{ height: 1, background: 'rgba(255,255,255,.07)', margin: '18px -20px' }} />;
}

export function Switch({ on }) {
  const s = on
    ? { bg: ACCENT, border: ACCENT, knob: '#001a25', x: 20 }
    : { bg: 'rgba(255,255,255,.06)', border: 'rgba(255,255,255,.35)', knob: 'rgba(255,255,255,.75)', x: 0 };
  return (
    <div style={{ width: 40, height: 20, borderRadius: 10, flex: 'none', padding: 2, background: s.bg, border: '1px solid ' + s.border, display: 'flex' }}>
      <div style={{ width: 14, height: 14, borderRadius: '50%', background: s.knob, marginLeft: s.x, transition: 'margin-left .12s ease' }} />
    </div>
  );
}

/** A setting that turns on or off. The whole row is the switch. Home and Settings both use it, so they look and work the same. */
export function ToggleRow({ on, label, help, onToggle, first }) {
  return (
    <div {...clickable(onToggle)} role="switch" aria-checked={!!on} className="hov-row"
      style={{ cursor: 'pointer', display: 'flex', alignItems: 'flex-start', gap: 14, padding: '16px 18px', borderTop: first ? 'none' : '1px solid rgba(255,255,255,.05)' }}>
      <div style={{ marginTop: 1 }}><Switch on={on} /></div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 14 }}>{label}</div>
        {help && <div style={{ fontSize: 12, lineHeight: 1.55, color: 'rgba(255,255,255,.545)', marginTop: 4 }}>{help}</div>}
      </div>
    </div>
  );
}

/** The pill-shaped On / Off choice. */
export function Segmented({ value, options, onChange }) {
  return (
    <div style={{ display: 'flex', gap: 4, padding: 3, borderRadius: 7, background: 'rgba(0,0,0,.25)', flex: 'none' }}>
      {options.map((o) => {
        const active = value === o.value;
        const accent = o.accent !== false;
        return (
          <div key={String(o.value)} {...clickable(() => onChange(o.value))}
            style={{
              cursor: 'pointer', fontSize: 13, padding: '6px 16px', borderRadius: 5,
              background: active ? (accent ? ACCENT : 'rgba(255,255,255,.12)') : 'transparent',
              color: active ? (accent ? '#001a25' : '#fff') : 'rgba(255,255,255,.7)',
              fontWeight: active ? 600 : 400
            }}>
            {o.label}
          </div>
        );
      })}
    </div>
  );
}

export function Button({ primary, danger, disabled, children, onClick, style }) {
  const base = primary
    ? { background: ACCENT, color: '#001a25', fontWeight: 600, border: '1px solid ' + ACCENT }
    : danger
      ? { background: 'rgba(224,108,108,.12)', border: '1px solid rgba(224,108,108,.4)', color: '#f09a9a' }
      : { background: 'rgba(255,255,255,.06)', border: '1px solid rgba(255,255,255,.09)', color: 'rgba(255,255,255,.85)' };
  const off = disabled ? { background: 'rgba(255,255,255,.04)', border: '1px solid rgba(255,255,255,.07)', color: 'rgba(255,255,255,.3)', cursor: 'default' } : {};
  return (
    <div {...clickable(disabled ? () => {} : onClick)} className={disabled ? '' : primary ? 'hov-accent' : 'hov-btn'}
      style={{ cursor: 'pointer', fontSize: 13, padding: '7px 16px', borderRadius: 5, flex: 'none', ...base, ...off, ...style }}>
      {children}
    </div>
  );
}

export function Select({ value, onChange, children, style }) {
  return (
    <select value={value} onChange={(e) => onChange(e.target.value)}
      style={{ font: 'inherit', fontSize: 13, color: '#fff', background: '#383838', border: '1px solid rgba(255,255,255,.1)', borderRadius: 5, padding: '6px 10px', flex: 'none', ...style }}>
      {children}
    </select>
  );
}

/** Button art for one input, or a text chip when there is no picture for this controller. */
export function Glyph({ img, text, size = 26 }) {
  if (img) return <div role="img" aria-label={text} style={{ width: size, height: size, backgroundImage: `url(${img})`, backgroundSize: 'contain', backgroundRepeat: 'no-repeat', backgroundPosition: 'center' }} />;
  return <div style={{ fontSize: 10, fontWeight: 700, padding: '4px 6px', borderRadius: 4, background: 'rgba(0,0,0,.3)', color: 'rgba(255,255,255,.8)' }}>{text}</div>;
}

export function Modal({ children, maxWidth = 520, z = 70, height, padding = 24 }) {
  return (
    <div data-modal="" style={{ position: 'fixed', inset: 0, zIndex: z, background: 'rgba(0,0,0,.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 32 }}>
      <div style={{ width: '100%', maxWidth, height, maxHeight: '84vh', display: 'flex', flexDirection: 'column', borderRadius: 9, background: '#2b2b2b', border: '1px solid rgba(255,255,255,.1)', boxShadow: '0 40px 80px rgba(0,0,0,.6)', overflow: 'hidden', animation: 'pp-pop .14s ease-out', padding }}>
        {children}
      </div>
    </div>
  );
}

export function Dialog({ dialog, onClose }) {
  // Escape closes it, which is also what B on the controller sends.
  useEffect(() => {
    if (!dialog) return undefined;
    const onEsc = (e) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onEsc);
    return () => window.removeEventListener('keydown', onEsc);
  }, [dialog, onClose]);

  if (!dialog) return null;
  return (
    <Modal>
      <div style={{ overflow: 'auto' }}>
        <div style={{ fontSize: 19, fontWeight: 600 }}>{dialog.title}</div>
        <div style={{ fontSize: 13, lineHeight: 1.6, color: 'rgba(255,255,255,.72)', marginTop: 8 }}>{dialog.body}</div>
        {(dialog.items || []).map((d, i) => (
          <div key={i} {...clickable(d.onClick)} className="hov-item"
            style={{ cursor: 'pointer', display: 'flex', alignItems: 'flex-start', gap: 12, padding: '13px 14px', borderRadius: 6, background: 'rgba(255,255,255,.04)', border: '1px solid rgba(255,255,255,.06)', marginTop: 8 }}>
            <div style={{ width: 26, height: 26, borderRadius: 5, flex: 'none', background: 'rgba(255,255,255,.07)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, fontWeight: 600, color: 'rgba(255,255,255,.7)' }}>{d.initial}</div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontSize: 13 }}>{d.label}</div>
              <div style={{ fontSize: 11, lineHeight: 1.5, color: 'rgba(255,255,255,.5)', marginTop: 3 }}>{d.detail}</div>
            </div>
          </div>
        ))}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 22 }}>
          <Button onClick={onClose}>{dialog.closeLabel || 'Close'}</Button>
        </div>
      </div>
    </Modal>
  );
}
