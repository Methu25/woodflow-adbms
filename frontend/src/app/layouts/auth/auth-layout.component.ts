import { afterNextRender, Component, ElementRef, Input, ViewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-auth-layout',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="wf-auth" [class.wf-setup]="setup" [class.wf-recovery]="recovery">
      <div class="wf-auth-content">
        <a [routerLink]="backLink" class="wf-back">{{ backLabel }}</a>
        <img class="wf-brand" [src]="setup ? '/assets/auth/237-1551/a2c43.svg' : '/assets/auth/226-1551/5ec46.svg'"
          alt="Wimalasiri Furniture — WoodFlow" />
        <p class="wf-eyebrow">WIMALASIRI FURNITURE · {{ section || (setup ? 'OWNER SETUP' : 'WORKSPACE LOGIN') }}</p>
        <h1 #heading tabindex="-1">{{ title }}</h1>
        <img class="wf-heading-rule" src="/assets/auth/226-1551/3e75c.svg" alt="" aria-hidden="true" />
        <section class="wf-auth-card" [attr.aria-label]="title"><ng-content /></section>
      </div>
      <p class="wf-craft">
        <img src="/assets/auth/226-1551/815a5.svg" alt="" aria-hidden="true" />
        Fine Ceylon Teak Craftsmanship · Workshop to Showroom
      </p>
      <p class="wf-auth-footer">Wimalasiri Furniture · WoodFlow</p>
    </div>
  `,
  styleUrl: './auth-layout.component.scss',
})
export class AuthLayoutComponent {
  @Input({ required: true }) title!: string;
  @Input() setup = false;
  @Input() recovery = false;
  @Input() backLink = '/splash';
  @Input() backLabel = '← Back to WoodFlow';
  @Input() section = '';
  @ViewChild('heading') heading?: ElementRef<HTMLHeadingElement>;
  constructor() {
    afterNextRender(() => this.heading?.nativeElement.focus({ preventScroll: true }));
  }
}
