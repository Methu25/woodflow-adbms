import { afterNextRender, Component, ElementRef, inject, Injector, OnDestroy, ViewChild, signal } from '@angular/core';

export interface UserFeedback {
  title: string; message: string; detail?: string;
  tone?: 'info' | 'warning' | 'danger'; confirmLabel?: string; cancelLabel?: string;
}
interface DialogState extends UserFeedback { kind: 'confirm' | 'success' | 'error'; }

@Component({
  selector: 'app-user-feedback-dialog', standalone: true,
  templateUrl: './user-feedback-dialog.component.html', styleUrl: './user-feedback-dialog.component.scss',
})
export class UserFeedbackDialogComponent implements OnDestroy {
  @ViewChild('modal', { static: true }) private modal!: ElementRef<HTMLDialogElement>;
  readonly state = signal<DialogState | null>(null);
  readonly opened = signal(false);
  private readonly injector = inject(Injector);
  private resolve?: (accepted: boolean) => void;
  private opener?: HTMLElement;
  confirm(options: UserFeedback) { return this.open({ ...options, kind: 'confirm' }); }
  notify(options: UserFeedback, kind: 'success' | 'error' = 'success') { return this.open({ ...options, kind }); }
  private open(state: DialogState): Promise<boolean> {
    if (this.opened()) return Promise.resolve(false);
    this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : undefined;
    this.state.set(state); this.opened.set(true);
    const answer = new Promise<boolean>(resolve => { this.resolve = resolve; });
    afterNextRender(() => {
      if (!this.opened()) return;
      this.modal.nativeElement.showModal();
      this.modal.nativeElement.querySelector<HTMLElement>('[data-initial-focus]')?.focus();
    }, { injector: this.injector });
    return answer;
  }
  finish(accepted: boolean) {
    if (!this.opened()) return;
    this.modal.nativeElement.close(); this.opened.set(false); this.state.set(null);
    const resolve = this.resolve; this.resolve = undefined; resolve?.(accepted);
    if (this.opener?.isConnected) this.opener.focus({ preventScroll: true });
  }
  cancel(event: Event) { event.preventDefault(); this.finish(this.state()?.kind !== 'confirm'); }
  ngOnDestroy() { this.finish(false); }
}
