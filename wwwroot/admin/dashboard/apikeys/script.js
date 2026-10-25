window.onload = function () {
    const form = document.getElementById("form");

    form.addEventListener("submit", async e => {
        e.preventDefault();

        const data = {
            name: form.name.value
        };

        const response = await fetch("/api/keys", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(data)
        });

        const responseData = await response.json();

        document.getElementById("key").textContent = "Here is your API key: " + responseData["key"];

        reload();
    })

    reload();
}

async function reload() {
    const response = await fetch('/api/keys', { method: 'GET' });

    if (response.status == 401) {
        var path = window.location.pathname
        window.location.pathname = path.substr(0, path.length - 10)
        return;
    }

    const data = await response.json();

    const tableBodyElem = document.getElementById("tablebody");

    tableBodyElem.innerHTML = "";

    const keys = Object.keys(data);
    for (var i = 0; i < keys.length; i++) {
        const key = keys[i];
        const value = data[key];

        var row = document.createElement("tr");

        var nameElem = document.createElement("td");
        nameElem.textContent = value.name;
        row.appendChild(nameElem);

        var idElem = document.createElement("td");
        idElem.textContent = value.id;
        row.appendChild(idElem);

        var createdElem = document.createElement("td");
        createdElem.textContent = new Date(value.created).toLocaleString();
        row.appendChild(createdElem);

        var lastUsedElem = document.createElement("td");
        var lastUsed = value.lastUsed;
        if (lastUsed == null)
            lastUsedElem.textContent = "N/A";
        else
            lastUsedElem.textContent = new Date(lastUsed).toLocaleString();
        row.appendChild(lastUsedElem);

        var actionsElem = document.createElement("td");

        var deleteElem = document.createElement("input");
        deleteElem.type = "submit";
        deleteElem.value = "Delete";
        deleteElem.style.width = "100%";
        deleteElem.onclick = async function () {
            const response = await fetch("/api/keys/" + value.id, {
                method: "DELETE",
                headers: {
                    "Content-Type": "application/json"
                }
            });
            reload();
        }

        actionsElem.appendChild(deleteElem);

        row.appendChild(actionsElem);

        tableBodyElem.appendChild(row);
    }
}