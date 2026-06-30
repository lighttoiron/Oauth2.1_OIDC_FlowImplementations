// The sign-in-options element exposes sign in options to the user.
// The user can choose to sign in with a full page redirect, or through a popup window
class SignInOptions extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <a href="/bff/login"><button>Sign In (Redirect)</button></a>
            <button id="popup-btn">Sign In (Popup)</button>
        `;
        this.shadowRoot.getElementById('popup-btn')
            .addEventListener('click', () => this.signInWithPopup());
    }

    signInWithPopup() {
        const popup = window.open('about:blank', 'bff_login_popup', 'width=500,height=650');

        if (!popup)
        {
            window.location.href = '/bff/login'; // If the popup window was blocked, perform a login by redirect instead
            return;
        }

        const channel = new BroadcastChannel('bff_login');
        let settled = false;

        const closeCheck = setInterval(() => {
            if (popup.closed && !settled) {
                clearInterval(closeCheck);
                channel.close();
                this.dispatchEvent(new CustomEvent('sign-in-error', {
                    bubbles: true, // Lets objects other than this object receive this event
                    composed: true, // Lets listeners that exist outside this element's shadow DOM receive this event
                    detail: {error: 'Popup closed before sign-in completed.'}
                }));
            }
        }, 500);

        channel.onmessage = (event) => {
            if (event.data?.type !== 'bff_login_result')
            {
                return;
            }

            settled = true;
            channel.close();
            clearInterval(closeCheck);

            if (event.data?.success) {
                this.dispatchEvent(new CustomEvent('signed-in', {
                    bubbles: true,
                    composed: true
                }));
            } else {
                this.dispatchEvent(new CustomEvent('sign-in-error', {
                    bubbles: true,
                    composed: true,
                    detail: {
                        error: event.data?.error
                    }
                }));
            }
        };

        popup.location.href = '/bff/login?popup=true';
    }
}

customElements.define('sign-in-options', SignInOptions);