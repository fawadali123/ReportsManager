/*
 * Loaded between jquery.validate and jquery.validate.unobtrusive.
 *
 * By default a field is re-validated when it loses focus. Clicking "Save" takes focus away from the
 * field being edited, its error message disappears, the button moves up a line, and the click misses it.
 * Validating on typing and on submit only keeps the layout still while the mouse is on its way.
 */
jQuery.validator.setDefaults({ onfocusout: false });
