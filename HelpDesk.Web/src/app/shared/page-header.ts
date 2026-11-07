import { Component, input } from '@angular/core';
@Component({
  selector: 'app-page-header',
  template: `<header class="workspace-heading">
    <div>
      <p class="eyebrow">{{ context() }}</p>
      <h1 [id]="headingId()">{{ title() }}</h1>
      @if (description()) {
        <p class="intro">{{ description() }}</p>
      }
    </div>
    <div class="heading-actions"><ng-content /></div>
  </header>`,
  styles: `
    :host {
      display: block;
    }
    .workspace-heading {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
      margin-bottom: 20px;
    }
    .workspace-heading > div {
      min-width: 0;
    }
    h1 {
      font-size: 24px;
      font-weight: 500;
      line-height: 1.25;
      margin: 0;
      letter-spacing: -0.6px;
      overflow-wrap: anywhere;
    }
    .eyebrow {
      margin: 0 0 6px;
      color: var(--muted);
      font-size: 12px;
    }
    .intro {
      color: var(--muted);
      font-size: 13px;
      line-height: 1.5;
      margin: 8px 0 0;
    }
    .heading-actions {
      display: flex;
      gap: 8px;
      flex-shrink: 0;
    }
    @media (max-width: 639px) {
      .workspace-heading {
        align-items: flex-start;
        flex-wrap: wrap;
      }
      h1 {
        font-size: 22px;
      }
    }
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly context = input('HelpDesk.NET');
  readonly description = input('');
  readonly headingId = input.required<string>();
}
