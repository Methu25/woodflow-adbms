import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  selector: 'app-access-restricted', standalone: true, imports: [RouterLink],
  template: `
    <section aria-labelledby="access-title">
      <div class="lock" aria-hidden="true"><img src="/assets/navigation/users.svg" alt="" /></div>
      <p class="eyebrow">WORKSPACE ACCESS</p>
      <h1 id="access-title">{{ unavailable ? 'Access check unavailable' : 'Access Restricted' }}</h1>
      <p>{{ unavailable ? 'We could not verify your access right now. Please try again shortly.' : 'Your current permissions do not include this page.' }}</p>
      <p>Contact the OWNER if you need access to this workspace.</p>
      <aside>Account administration, security settings and audit administration are reserved for authorized owners.</aside>
      <a routerLink="/dashboard">Check dashboard access again</a>
    </section>
  `,
  styles: [`
    :host{display:block;color:#073763}section{max-width:660px;margin:50px auto;padding:clamp(24px,5vw,52px);border:1px solid #dcebfa;border-radius:20px;background:linear-gradient(145deg,#fff,#f3f8fe);text-align:center}
    .lock{display:grid;place-items:center;width:72px;height:72px;margin:0 auto 24px;border:10px solid #dcebfa;border-radius:50%;background:linear-gradient(135deg,#1685c5,#08a9c9)}
    h1{font-size:30px;line-height:1.3}p,aside{font-size:13px;line-height:1.8;color:#526b85}.eyebrow{font-size:10px;font-weight:700;letter-spacing:.08em}aside{padding:20px;background:#eaf3fc;border:1px solid #dcebfa;border-radius:12px;margin:24px 0}
    a{display:inline-block;padding:14px 20px;border-radius:9px;background:#1685c5;color:white;text-decoration:none;font-size:13px}a:focus-visible{outline:3px solid #073763;outline-offset:4px}
  `],
})
export class AccessRestrictedComponent {
  readonly unavailable = inject(ActivatedRoute).snapshot.queryParamMap.get('unavailable') === '1';
}
