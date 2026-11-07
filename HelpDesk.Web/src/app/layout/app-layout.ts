import { WorkspaceIcon } from '../shared/workspace-icon';
import { DemoService } from '../core/demo/demo.service';
import { Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { SessionMenu } from './session-menu';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [WorkspaceIcon, RouterLink, RouterLinkActive, RouterOutlet, SessionMenu],
  templateUrl: './app-layout.html',
  styleUrl: './app-layout.scss',
})
export class AppLayout {
  readonly demo = inject(DemoService);
  readonly auth = inject(AuthService);
  readonly menuOpen = signal(false);
  readonly ticketSection = signal(false);
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly mainContent = viewChild<ElementRef<HTMLElement>>('mainContent');

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  @HostListener('document:keydown.escape')
  closeMenu(): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    this.menuButton()?.nativeElement.focus();
  }

  selectRoute(event: MouseEvent): void {
    if (!(event.target instanceof Element) || !event.target.closest('a')) return;
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    if (this.menuOpen()) {
      this.menuOpen.set(false);
      this.mainContent()?.nativeElement.focus();
    }
  }
  readonly breadcrumb = signal('Inicio');
  private readonly router = inject(Router);
  constructor() {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        let route = this.router.routerState.snapshot.root;
        while (route.firstChild) route = route.firstChild;
        this.breadcrumb.set(String(route.data['label'] ?? 'Inicio'));
        this.ticketSection.set(/^\/tickets(?:\/\d+)?(?:[?#]|$)/.test(this.router.url));
        if (this.menuOpen()) {
          this.menuOpen.set(false);
          this.mainContent()?.nativeElement.focus();
        }
      });
  }
}
