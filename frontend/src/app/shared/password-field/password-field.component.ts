import { Component, Input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

@Component({
  selector: 'app-password-field',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="wf-field">
      <label [for]="inputId">{{ label }} @if (hint) { <small>{{ hint }}</small> }</label>
      <div class="wf-input" [class.invalid]="!!error">
        <img src="/assets/auth/226-1551/3ef22.svg" alt="" aria-hidden="true" />
        <input [id]="inputId" [type]="visible() ? 'text' : 'password'" [formControl]="control"
          [attr.autocomplete]="autocomplete" maxlength="128"
          [attr.aria-invalid]="!!error" [attr.aria-describedby]="error ? inputId + '-error' : null" />
        <button type="button" class="wf-reveal" (click)="visible.set(!visible())"
          [attr.aria-label]="(visible() ? 'Hide ' : 'Show ') + label.toLowerCase()"
          [attr.aria-pressed]="visible()">
          <img src="/assets/auth/226-1551/b993b.svg" alt="" aria-hidden="true" />
        </button>
      </div>
      @if (error) { <p class="wf-field-error" [id]="inputId + '-error'">{{ error }}</p> }
    </div>
  `,
})
export class PasswordFieldComponent {
  @Input({ required: true }) control!: FormControl<string>;
  @Input({ required: true }) inputId!: string;
  @Input() label = 'Password';
  @Input() hint = '';
  @Input() error = '';
  @Input() autocomplete = 'current-password';
  readonly visible = signal(false);
}
