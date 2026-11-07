import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppLayout } from './app-layout';

@Component({ template: '' })
class Destination {}

describe('Workspace navigation disclosure', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [AppLayout],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: '', component: Destination, data: { label: 'Inicio' } },
          { path: 'other', component: Destination, data: { label: 'Otra sección' } },
          { path: 'tickets/new', component: Destination },
          { path: 'tickets/:id', component: Destination },
        ]),
      ],
    }),
  );
  function setup() {
    const fixture = TestBed.createComponent(AppLayout);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const button = page.querySelector<HTMLButtonElement>('.menu-toggle')!;
    // jsdom does not apply responsive media queries: expose the mobile trigger.
    button.style.display = 'inline-flex';
    return { fixture, page, button };
  }
  it('exposes a native button controlling the collapsed navigation', () => {
    const { page, button } = setup();
    expect(button.type).toBe('button');
    expect(button.getAttribute('aria-label')).toBe('Abrir menú');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.getAttribute('aria-controls')).toBe(page.querySelector('aside.sidebar')!.id);
    expect(page.querySelector('.sidebar.is-open')).toBeNull();
  });
  it('toggles without moving focus from its button', () => {
    const { fixture, page, button } = setup();
    button.focus();
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.getAttribute('aria-label')).toBe('Cerrar menú');
    expect(page.querySelector('.sidebar.is-open')).not.toBeNull();
    expect(document.activeElement).toBe(button);
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
  });
  it('closes with Escape from a link and restores focus', () => {
    const { fixture, page, button } = setup();
    button.click();
    fixture.detectChanges();
    const link = page.querySelector<HTMLAnchorElement>('.sidebar nav a')!;
    link.focus();
    link.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(button);
  });
  it('does not steal focus when Escape is pressed while closed', () => {
    const { page } = setup();
    const main = page.querySelector<HTMLElement>('main')!;
    main.focus();
    main.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(document.activeElement).toBe(main);
  });
  it('closes on selection of an already active route and focuses content', async () => {
    const { fixture, page, button } = setup();
    await TestBed.inject(Router).navigateByUrl('/');
    button.click();
    fixture.detectChanges();
    page.querySelector<HTMLAnchorElement>('.sidebar nav a[href="/"]')!.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(page.querySelector('main'));
    await fixture.whenStable();
  });
  it('closes on completed navigation and updates the section', async () => {
    const { fixture, page, button } = setup();
    button.click();
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/other');
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(page.querySelector('.breadcrumb strong')!.textContent).toBe('Otra sección');
    expect(document.activeElement).toBe(page.querySelector('main'));
  });
  it('keeps the ticket section active for a detail but separates creation', async () => {
    const { fixture } = setup();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tickets/12?status=Open');
    expect(fixture.componentInstance.ticketSection()).toBe(true);
    await router.navigateByUrl('/tickets/new');
    expect(fixture.componentInstance.ticketSection()).toBe(false);
  });
  it('preserves the disclosure for modified clicks opening another tab', () => {
    const { fixture, page, button } = setup();
    button.click();
    fixture.detectChanges();
    page
      .querySelector<HTMLAnchorElement>('.sidebar nav a')!
      .dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true, button: 0 }));
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });
});
