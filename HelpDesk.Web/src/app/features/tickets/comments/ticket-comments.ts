import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { requiredTicketText } from '../ticket-form.validation';
import { CommentService, TicketComment, commentError } from './comment.service';

@Component({
  selector: 'app-ticket-comments',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './ticket-comments.html',
  styleUrls: ['../ticket-page.scss', '../create/ticket-create.scss', './ticket-comments.scss'],
})
export class TicketComments {
  readonly ticketId = input.required<number>();
  private readonly service = inject(CommentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly contextChanged = new Subject<void>();
  readonly opened = signal(false);
  readonly items = signal<TicketComment[]>([]);
  readonly reading = signal(false);
  readonly pending = signal(false);
  readonly loaded = signal(false);
  readonly hasMore = signal(false);
  readonly nextBeforeId = signal<number | null>(null);
  readonly readError = signal('');
  readonly message = signal('');
  readonly success = signal(false);
  readonly reviewRequired = signal(false);
  readonly content = new FormControl('', {
    nonNullable: true,
    validators: [
      requiredTicketText,
      Validators.maxLength(4000),
      (c) => (c.value.includes('\0') ? { nullCharacter: true } : null),
    ],
  });
  readonly form = new FormGroup({ content: this.content });

  ngOnChanges() {
    this.contextChanged.next();
    this.opened.set(false);
    this.items.set([]);
    this.reading.set(false);
    this.pending.set(false);
    this.loaded.set(false);
    this.hasMore.set(false);
    this.nextBeforeId.set(null);
    this.readError.set('');
    this.message.set('');
    this.success.set(false);
    this.reviewRequired.set(false);
    this.content.reset();
  }
  open() {
    this.opened.set(true);
    this.load();
  }
  load(older = false) {
    if (this.reading() || this.pending() || (older && !this.hasMore())) return;
    const id = this.ticketId();
    this.reading.set(true);
    this.readError.set('');
    if (!older) {
      this.items.set([]);
      this.loaded.set(false);
      this.message.set('');
      this.success.set(false);
    }
    this.service
      .list(id, older ? this.nextBeforeId() : null)
      .pipe(takeUntil(this.contextChanged), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.items.update((items) => (older ? [...items, ...page.items] : page.items));
          this.hasMore.set(page.hasMore);
          this.nextBeforeId.set(page.nextBeforeId);
          this.loaded.set(true);
          this.reading.set(false);
          this.reviewRequired.set(false);
        },
        error: (error) => {
          this.reading.set(false);
          this.readError.set(commentError(error));
          this.loaded.set(false);
          this.items.set([]);
          this.hasMore.set(false);
          this.nextBeforeId.set(null);
        },
      });
  }
  publish() {
    if (this.pending() || this.reading() || !this.loaded() || this.reviewRequired()) return;
    this.content.markAsTouched();
    if (this.content.invalid) return;
    this.pending.set(true);
    this.message.set('');
    this.success.set(false);
    this.service
      .create(this.ticketId(), this.content.value)
      .pipe(takeUntil(this.contextChanged), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (comment) => {
          this.pending.set(false);
          this.content.reset();
          this.success.set(true);
          this.message.set('Comentario publicado correctamente.');
          this.items.update((items) => [comment, ...items.filter((c) => c.id !== comment.id)]);
        },
        error: (error) => {
          this.pending.set(false);
          this.reviewRequired.set(true);
          this.message.set(commentError(error, true));
          // A denial can mean the ticket was reassigned; stop showing the old thread.
          if (error?.status === 401 || error?.status === 403 || error?.status === 404) {
            this.items.set([]);
            this.loaded.set(false);
            this.hasMore.set(false);
            this.nextBeforeId.set(null);
          }
        },
      });
  }
}
