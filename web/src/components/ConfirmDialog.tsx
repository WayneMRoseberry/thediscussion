import { useEffect, useId, useRef } from 'react';

interface ConfirmDialogProps {
  title: string;
  message: string;
  confirmLabel: string;
  /** Shown while the confirmed action runs. */
  busyLabel: string;
  busy: boolean;
  /** Why the last attempt failed; the confirm button then retries. */
  error?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * A modal confirmation for destructive actions. Focus moves to Cancel when it opens, Escape cancels, and focus returns
 * to where it was when it closes. After a failure it shows the error and offers Retry.
 */
export function ConfirmDialog({ title, message, confirmLabel, busyLabel, busy, error, onConfirm, onCancel }: ConfirmDialogProps) {
  const id = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    cancelRef.current?.focus();
    return () => {
      if (opener?.isConnected) {
        opener.focus();
      }
    };
  }, []);

  return (
    <div className="dialog-backdrop">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        aria-describedby={`${id}-message`}
        className="dialog"
        onKeyDown={(event) => {
          if (event.key === 'Escape' && !busy) {
            event.stopPropagation();
            onCancel();
            return;
          }
          if (event.key === 'Tab') {
            // Keep focus inside the dialog while it is open.
            const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));
            const first = buttons[0];
            const last = buttons[buttons.length - 1];
            if (!first || !last) {
              event.preventDefault();
            } else if (event.shiftKey && document.activeElement === first) {
              event.preventDefault();
              last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
              event.preventDefault();
              first.focus();
            }
          }
        }}
      >
        <h2 id={`${id}-title`}>{title}</h2>
        <p id={`${id}-message`}>{message}</p>
        {error && (
          <p role="alert" className="form-error">
            {error}
          </p>
        )}
        <div className="form-actions">
          <button type="button" className="button-danger" onClick={onConfirm} disabled={busy}>
            {busy ? busyLabel : error ? 'Retry' : confirmLabel}
          </button>
          <button type="button" className="button-secondary" ref={cancelRef} onClick={onCancel} disabled={busy}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}
