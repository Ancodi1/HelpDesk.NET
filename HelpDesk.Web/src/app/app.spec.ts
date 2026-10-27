import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { App } from './app';
import { routes } from './app.routes';
import { HealthService } from './core/api/health.service';

describe('Application navigation', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        { provide: HealthService, useValue: { check: () => of({ status: 'ok' }) } },
      ],
    });
  });
  it('renders the home inside the accessible application shell', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('main#contenido')).not.toBeNull();
    expect(page.querySelector('h1')?.textContent).toContain('Mejores conexiones');
    expect(page.querySelector('[aria-current="page"]')?.textContent).toContain('Inicio');
    expect(page.querySelector('.skip-link')?.getAttribute('href')).toBe('#contenido');
  });
  it('redirects unknown routes to home', async () => {
    TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/ruta-inexistente');
    expect(router.url).toBe('/');
  });
});
