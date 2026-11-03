import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../../app';
import { AuthService } from '../../../core/auth/auth.service';
import { TicketDetail } from './ticket-detail';
import { TicketManagement } from './ticket-management';
import { ticketFixture } from '../ticket.testing';

it('updates the visible detail and keeps list context after a write', async () => {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'tickets/:id', component: TicketDetail }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { user: signal({ id: 7, role: 'Admin' }) } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const router = TestBed.inject(Router);
  const f = TestBed.createComponent(App);
  await router.navigateByUrl('/tickets/41?priority=High&page=2');
  f.detectChanges();
  http.expectOne('/api/tickets/41').flush(ticketFixture());
  f.detectChanges();
  const management = f.debugElement.query(By.directive(TicketManagement))
    .componentInstance as TicketManagement;
  management.text.controls.title.setValue('Updated fixture title');
  management.save();
  http.expectOne('/api/tickets/41').flush(ticketFixture({ title: 'Updated fixture title' }));
  f.detectChanges();
  expect(f.nativeElement.querySelector('h1').textContent).toBe('Updated fixture title');
  expect(f.nativeElement.textContent).toContain('Cambios guardados correctamente');
  expect(f.nativeElement.querySelector('.back-link').getAttribute('href')).toBe(
    '/tickets?priority=High&page=2',
  );
  f.destroy();
  http.verify();
});
