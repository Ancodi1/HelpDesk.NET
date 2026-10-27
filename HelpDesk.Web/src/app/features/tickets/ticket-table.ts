import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { categoryLabels, priorityLabels, statusLabels, Ticket } from './ticket.models';

@Component({
  selector: 'app-ticket-table',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './ticket-table.html',
  styleUrl: './ticket-table.scss',
})
export class TicketTable {
  readonly tickets = input.required<readonly Ticket[]>();
  readonly statusNames = statusLabels;
  readonly priorityNames = priorityLabels;
  readonly categoryNames = categoryLabels;
}
