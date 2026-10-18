# Restricting Access

A **User** page defaults to **Access: All Users**, meaning every signed in account. Switch **Access** to **Specific Users**, pick accounts, and the page is served to those accounts and nobody else.

A page limited to two accounts:

![Specific users picker](screenshots/Restricted%20Page.png)

The check runs on the content endpoint, after Jellyfin has authenticated the request and after the tier check, and before the page body is composed. A viewer the list does not name gets a 403 and the sign in shell shows them the not authorized card. They never receive the page's HTML, CSS, or JavaScript, so anything the page's source contains is only ever transmitted to an account you picked.

## Details Worth Knowing

* **Administrators always have access** and are not offered in the picker. They author these pages and can read any page's source from the dashboard regardless, so excluding one would be a restriction the dashboard could not actually keep.
* The list is only accepted on the **User** tier. Saving one against **Anyone** or **Admin** is refused, because it would be ignored at serve time while the dashboard implied the page was restricted.
* An empty list means all users at the tier. The dashboard will not let you save **Specific Users** with nobody picked, so the two states cannot be confused.
* An API key authenticates a caller without identifying a user, so a restricted page refuses API key requests. If a stored list is somehow unreadable, it admits nobody rather than everybody.

!!! note
    This narrows who receives a page. It does not hide the page's source from the people who do receive it, since a browser has to be given the source to render it. Anything in a page is readable by every account you grant access to.
