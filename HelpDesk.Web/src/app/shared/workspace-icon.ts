import { Component, input } from '@angular/core';

type IconName =
  | 'tickets'
  | 'assigned'
  | 'plus'
  | 'users'
  | 'session'
  | 'home'
  | 'connection'
  | 'info'
  | 'menu'
  | 'close';
@Component({
  selector: 'app-workspace-icon',
  template: `<svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    aria-hidden="true"
  >
    <path [attr.d]="paths[name()]" />
  </svg>`,
  styles: `
    :host {
      display: inline-flex;
      flex: 0 0 18px;
    }
    svg {
      width: 18px;
      height: 18px;
    }
  `,
})
export class WorkspaceIcon {
  readonly name = input.required<IconName>();
  readonly paths: Record<IconName, string> = {
    tickets: 'M5 3h14v18H5z M8 8h8 M8 12h8 M8 16h5',
    assigned: 'M4 5h11v14H4z M8 10l2 2 5-5 M18 12h3 M18 16h3',
    plus: 'M12 4v16 M4 12h16',
    users:
      'M9 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M2 21v-2a7 7 0 0 1 14 0v2 M17 5a4 4 0 0 1 0 7 M18 15a6 6 0 0 1 4 6',
    session: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M4 21v-2a8 8 0 0 1 16 0v2',
    connection: 'M4 4h16v6H4z M4 14h16v6H4z M7 7h1 M7 17h1 M12 7h5 M12 17h5',
    info: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M12 10v7 M12 7v1',
    menu: 'M4 6h16 M4 12h16 M4 18h16',
    close: 'M6 6l12 12 M18 6L6 18',
    home: 'M3 11l9-8 9 8 M5 10v11h5v-7h4v7h5V10',
  };
}
