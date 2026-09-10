import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RoomNameField } from './room-name-field';

/** Hosts the field the way a create form does, with two-way bindings onto plain values. */
@Component({
  imports: [RoomNameField],
  template: `
    <app-room-name-field
      controlId="testName"
      labelKey="home.sessionName"
      placeholderKey="home.sessionNamePlaceholder"
      [(name)]="name"
      [(appendDate)]="appendDate"
    />
  `,
})
class Host {
  name = signal('');
  appendDate = signal(false);
}

describe('RoomNameField', () => {
  afterEach(() => TestBed.resetTestingModule());

  function render(name = '', appendDate = false) {
    TestBed.configureTestingModule({ imports: [Host] });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.name.set(name);
    fixture.componentInstance.appendDate.set(appendDate);
    fixture.detectChanges();
    return {
      fixture,
      host: fixture.componentInstance,
      el: fixture.nativeElement as HTMLElement,
      input: () => fixture.nativeElement.querySelector('#testName') as HTMLInputElement,
      tickbox: () =>
        fixture.nativeElement.querySelector('#testName-append-date') as HTMLInputElement,
      preview: () => fixture.nativeElement.querySelector('#testName-preview') as HTMLElement | null,
    };
  }

  it('labels the input with the tool’s own word for the name', () => {
    const { el, input } = render();

    expect(el.querySelector<HTMLLabelElement>('label[for="testName"]')!.textContent)
      .toContain('Session name');
    expect(input().placeholder).toBe('e.g. Sprint 24 backlog');
  });

  it('shows the name it was given', () => {
    expect(render('Team Dragon').input().value).toBe('Team Dragon');
  });

  it('reports typing back to the form', () => {
    const { host, input, fixture } = render();

    input().value = 'Team Dragon';
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(host.name()).toBe('Team Dragon');
  });

  it('reports the tickbox back to the form', () => {
    const { host, tickbox, fixture } = render('Team Dragon');

    tickbox().click();
    fixture.detectChanges();

    expect(host.appendDate()).toBe(true);
  });

  it('reflects a ticked box it was given', () => {
    expect(render('Team Dragon', true).tickbox().checked).toBe(true);
  });

  // --- The preview --------------------------------------------------------

  it('previews what the room will actually be called', () => {
    const { preview } = render('Team Dragon', true);

    const today = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    const stamp = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;

    expect(preview()!.textContent).toContain(`Team Dragon ${stamp}`);
  });

  it('shows no preview when the stamp is off', () => {
    // There is nothing to preview: the name is already what it will be called.
    expect(render('Team Dragon', false).preview()).toBeNull();
  });

  it('shows no preview when there is no name to stamp', () => {
    expect(render('   ', true).preview()).toBeNull();
  });

  it('updates the preview as the name is typed', () => {
    const { input, preview, fixture } = render('', true);
    expect(preview()).toBeNull();

    input().value = 'Team Dragon';
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(preview()!.textContent).toContain('Team Dragon 20');
  });

  it('previews through the same composer that creates the room', () => {
    // A date already on the end is replaced, not added to — so the preview cannot promise a name
    // the create call would not produce.
    const { preview } = render('Sprint review 2020-01-01', true);

    expect(preview()!.textContent).not.toContain('2020-01-01');
  });

  it('names the tickbox and preview after the control, so labels stay tied', () => {
    const { el } = render('Team Dragon', true);

    expect(el.querySelector('label[for="testName-append-date"]')).not.toBeNull();
    expect(el.querySelector('#testName-preview')).not.toBeNull();
  });
});
