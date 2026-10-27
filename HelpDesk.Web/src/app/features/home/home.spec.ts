import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Home } from './home';

describe('Home connection panel', () => {
  let fixture: ComponentFixture<Home>;
  let http: HttpTestingController;
  let page: HTMLElement;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Home);
    page = fixture.nativeElement;
    fixture.detectChanges();
  });
  afterEach(() => {
    fixture.destroy();
    http.verify();
  });
  it('announces loading and prevents duplicate checks', () => {
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Conectando');
    expect(page.querySelector('button')?.disabled).toBe(true);
    fixture.componentInstance.checkConnection();
    http.expectOne('/api/health').flush({ status: 'ok' });
  });
  it('announces success after a valid response', async () => {
    http.expectOne('/api/health').flush({ status: 'ok' });
    await fixture.whenStable();
    expect(page.querySelector('[role="status"]')?.textContent).toContain('La API está disponible');
    expect(page.querySelector('button')?.disabled).toBe(false);
    expect(page.textContent).toContain('Última comprobación:');
  });
  it('shows a recoverable error and allows a successful retry', async () => {
    http.expectOne('/api/health').error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(page.querySelector('[role="status"]')?.textContent).toContain(
      'No hemos podido conectar',
    );
    expect(page.querySelector('button')?.textContent).toContain('Reintentar');
    page.querySelector('button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('button')?.disabled).toBe(true);
    http.expectOne('/api/health').flush({ status: 'ok' });
    await fixture.whenStable();
    expect(page.querySelector('[role="status"]')?.textContent).toContain('La API está disponible');
  });
  it('does not display an invalid payload as success', async () => {
    http.expectOne('/api/health').flush({ status: 'unexpected' });
    await fixture.whenStable();
    expect(page.querySelector('[role="status"]')?.textContent).toContain(
      'No hemos podido conectar',
    );
  });
  it('cancels the pending request when the page is destroyed', () => {
    const request = http.expectOne('/api/health');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
