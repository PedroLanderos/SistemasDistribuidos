/*

	WSClient.js

	Copyright(C) 2015-2025  Carlos Pineda Guerrero
	carlospinedag@m4gm.com

	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/


function WSClient(url)
{
	this.url = url;

	this.get = function(operacion, args, callback) 
	{
		try
		{
			var pares = [];
			var nombre;
			for (nombre in args)
			{
				let value = args[nombre];				
				if (typeof value !== "string") value = JSON.stringify(value);
				pares.push(nombre + "=" + encodeURI(value).replace(/=/g, "%3D").replace(/&/g, "%26").replace(/%20/g, "+"));
			}
			var parametros = pares.join("&");			
			fetch(url + "/" + operacion + "?" + parametros,
			{
				method: "GET",
				headers:
				{
					"Content-Type": "application/x-www-form-urlencoded",
				},
			})
    		.then(respuesta => respuesta.json().then(data =>
    		{
    			if (callback != null) callback(respuesta.status, data);
    		}))
			.catch((error) => {
				alert("Error: " + error.message);
			});
		} catch (e)
		{
			alert("Error: " + e.message);
		}
	};

	this.post = function(operacion,body,callback)
	{
		fetch(url + "/" + operacion, 
		{
			method: "POST",
			headers:
			{
				"Content-Type": "application/json"
			},
			body: JSON.stringify(body)
		})
		.then(respuesta => respuesta.json().then(data => 
		{
			if (callback != null) callback(respuesta.status, data);
		}))
		.catch(error => 
		{
			alert("Error: " + error.message);
		});
	};

	this.put = function(operacion,args,body,callback)
	{
		var pares = [];
		var nombre;
		for (nombre in args)
		{
			var value = args[nombre];
			if (typeof value !== "string") value = JSON.stringify(value);
			pares.push(nombre + '=' + encodeURI(value).replace(/=/g,'%3D').replace(/&/g,'%26').replace(/%20/g,'+'));
		}
		var parametros = pares.join('&');
		fetch(url + "/" + operacion + "?" + parametros,
		{
			method: "PUT",
			headers:
			{
				"Content-Type": "application/json"
			},
			body: JSON.stringify(body)
		})
		.then(respuesta => respuesta.json().then(data => 
		{
			if (callback != null) callback(respuesta.status, data);
		}))
		.catch(error => 
		{
			alert("Error: " + error.message);
		});
	};

	this.delete = function(operacion, args, callback)
	{
		try
		{
			var pares = [];
			var nombre;
			for (nombre in args)
			{
				let value = args[nombre];
				if (typeof value !== "string") value = JSON.stringify(value);
				pares.push(nombre + "=" + encodeURI(value).replace(/=/g, "%3D").replace(/&/g, "%26").replace(/%20/g, "+"));
			}
			var parametros = pares.join("&");
			fetch(url + "/" + operacion + "?" + parametros,
			{
				method: "DELETE",
				headers:
				{
					"Content-Type": "application/x-www-form-urlencoded",
				},
			})
    		.then(respuesta => respuesta.json().then(data => 
	    	{
    			if (callback != null) callback(respuesta.status, data);
    		}))
			.catch((error) => 
			{
				alert("Error: " + error.message);
			});
		}
		catch (e)
		{
			alert("Error: " + e.message);
		}
	};
}
