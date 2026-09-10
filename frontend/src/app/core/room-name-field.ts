import { Component, computed, inject, input, model } from '@angular/core';
import { RoomNameService } from './room-name.service';
import { TranslatePipe } from './translate.pipe';

/**
 * The room-name field every create form has: the name, and a tickbox that date-stamps it.
 *
 * Shared rather than copied four times, for the reason `RoomNameService` is: the field is identical
 * in all four forms, and the interesting part — the live preview of what the room will actually be
 * called — is the part worth having exactly one of. A fifth tool drops this element in and gets the
 * behaviour, the preview and the a11y wiring with it.
 *
 * Deliberately **not** an `ngModel` control. The value reaches the parent through `model()`, and no
 * form validation depends on this input, so a plain value/input pair avoids registering a control in
 * the parent's template-driven form for nothing.
 */
@Component({
  selector: 'app-room-name-field',
  imports: [TranslatePipe],
  template: `
    <div class="mb-3">
      <label class="form-label" [attr.for]="controlId()">{{ labelKey() | t }}</label>
      <input
        class="form-control"
        type="text"
        autocomplete="off"
        [id]="controlId()"
        [value]="name()"
        [placeholder]="placeholderKey() | t"
        (input)="name.set($any($event.target).value)"
      />

      <div class="form-check mt-2">
        <input
          class="form-check-input"
          type="checkbox"
          [id]="controlId() + '-append-date'"
          [checked]="appendDate()"
          (change)="appendDate.set($any($event.target).checked)"
        />
        <label class="form-check-label" [attr.for]="controlId() + '-append-date'">
          {{ 'room.appendDate' | t }}
        </label>
      </div>

      @if (preview(); as stamped) {
        <div class="form-text" [id]="controlId() + '-preview'">
          {{ 'room.appendDatePreview' | t: { name: stamped } }}
        </div>
      }
    </div>
  `,
})
export class RoomNameField {
  private readonly names = inject(RoomNameService);

  /** i18n key for this tool's own word for the name — "Session name", "Retro name", … */
  readonly labelKey = input.required<string>();

  readonly placeholderKey = input.required<string>();

  /** Ties the label to the input, and names the tickbox and preview after it. */
  readonly controlId = input('roomName');

  readonly name = model.required<string>();

  readonly appendDate = model.required<boolean>();

  /**
   * What the room will be called, shown only when the stamp is on and there is a name to stamp.
   * Composed by the same method that composes it for real, so the preview cannot drift from the
   * result — including the date already being replaced rather than added to.
   */
  protected readonly preview = computed(() => {
    if (!this.appendDate()) {
      return null;
    }
    const composed = this.names.compose(this.name(), true);
    return composed || null;
  });
}
